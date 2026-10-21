using System.Globalization;
using System.Security.Claims;

/// <summary>
/// B5 stage 6: the mobile app's backend for frontend. The home screen needs the balance,
/// recent activity, payees and card top-ups, from three services. Instead of the app making
/// four calls over a mobile network, it makes one, and the gateway makes the four inside the
/// data centre, in parallel, and returns only what the screen shows.
/// </summary>
static class Home
{
    public static void AddHome(this IServiceCollection services, IConfiguration config)
    {
        foreach (var cluster in new[] { "ledger", "payees", "statements" })
        {
            services.AddHttpClient(cluster, http =>
            {
                http.BaseAddress = new Uri(config[$"ReverseProxy:Clusters:{cluster}:Destinations:main:Address"]!);
                http.Timeout = TimeSpan.FromSeconds(2);
            });
        }
    }

    // #region home
    public static void MapHome(this IEndpointRouteBuilder app) =>
        app.MapGet("/app/home", async (ClaimsPrincipal user, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var account = int.Parse(user.FindFirstValue(Lab.AccountClaim)!, CultureInfo.InvariantCulture);
            var now = DateTimeOffset.UtcNow;
            var unavailable = new List<string>();

            // Each part is optional: if one service can't answer, the screen shows the rest.
            async Task<T?> Part<T>(string cluster, string path, string name) where T : class
            {
                try { return await clients.CreateClient(cluster).GetFromJsonAsync<T>(path, ct); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    lock (unavailable) unavailable.Add(name);
                    return null;
                }
            }

            var balance = Part<AccountPart>("ledger", $"/accounts/{account}", "balance");
            var statement = Part<StatementPart>("statements", $"/accounts/{account}/statements/{now.Year}/{now.Month}", "recent");
            var payees = Part<List<PayeePart>>("payees", $"/accounts/{account}/payees", "payees");
            var topUps = Part<List<TopUpPart>>("ledger", $"/accounts/{account}/card-top-ups", "cardTopUps");
            await Task.WhenAll(balance, statement, payees, topUps);

            return Results.Ok(new
            {
                balance = (await balance)?.Balance,
                recent = (await statement)?.Lines.TakeLast(5).Reverse(),
                payees = (await payees)?.Select(p => new { p.PayeeId, p.Name }),
                cardTopUps = (await topUps)?.TakeLast(3).Reverse(),
                unavailable,
            });
        })
        .RequireAuthorization("customer")
        .RequireRateLimiting("per-customer");
    // #endregion home

    sealed record AccountPart(decimal Balance);
    sealed record StatementPart(List<LinePart> Lines);
    sealed record LinePart(DateTimeOffset PostedAt, string Reference, decimal Amount);
    sealed record PayeePart(Guid PayeeId, string Name);
    sealed record TopUpPart(Guid TopUpId, string CardId, decimal Amount, string Status);
}
