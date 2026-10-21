using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Yarp.ReverseProxy.Model;

/// <summary>
/// B5 stage 7: moving payees from the monolith's module (destination "main") to the new payees
/// service (destination "service"), a step at a time. Reads move customer by customer; reads can
/// be compared between the two in the background; writes move all at once, after a short pause
/// in which the new owner catches up. Each step can be reversed. Driven by the /lab endpoints.
/// </summary>
sealed class PayeesMigration(IHttpClientFactory clients, IConfiguration config)
{
    public const string Main = "main";
    public const string Service = "service";

    private int _readPercent;
    private int _inFlightWrites;
    private long _compared;
    private long _mismatched;
    private readonly ConcurrentQueue<string> _samples = new();

    public bool Shadow { get; set; }
    public string WritesTo { get; private set; } = Main;
    public bool WritesPaused { get; private set; }

    public int ReadPercent
    {
        get => Volatile.Read(ref _readPercent);
        set => Volatile.Write(ref _readPercent, Math.Clamp(value, 0, 100));
    }

    public object Status => new
    {
        readPercent = ReadPercent, shadow = Shadow, writesTo = WritesTo, writesPaused = WritesPaused,
        compared = Interlocked.Read(ref _compared), mismatched = Interlocked.Read(ref _mismatched), samples = _samples.Take(5),
    };

    // #region route-payees
    /// <summary>Sends a payees request to the store the migration says, or asks writes to wait.</summary>
    public async Task RouteAsync(HttpContext context, Func<Task> next)
    {
        var proxy = context.GetReverseProxyFeature();
        var account = int.Parse((string)context.GetRouteValue("accountId")!, CultureInfo.InvariantCulture);
        var isRead = HttpMethods.IsGet(context.Request.Method);
        if (!isRead)
        {
            // Count the write in before checking for a pause, so a pause never misses one in flight.
            Interlocked.Increment(ref _inFlightWrites);
            if (WritesPaused)
            {
                Interlocked.Decrement(ref _inFlightWrites);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "1";
                return;
            }
        }
        try
        {
            // Reads move customer by customer, and each customer always sees the same store.
            var target = isRead ? (account % 100 < ReadPercent ? Service : Main) : WritesTo;
            proxy.AvailableDestinations = [.. proxy.AllDestinations.Where(d => d.DestinationId == target)];
            await next();
        }
        finally
        {
            if (!isRead) Interlocked.Decrement(ref _inFlightWrites);
        }
        if (isRead && Shadow && context.Response.StatusCode == StatusCodes.Status200OK)
            _ = CompareAsync(context.Request.Path + context.Request.QueryString, account);
    }
    // #endregion route-payees

    // #region shadow
    /// <summary>
    /// Asks both stores the same question and compares the answers. A payee added or removed
    /// between the two reads, or not yet copied, isn't a real difference, so a mismatch is
    /// checked again, once the copy has had time to catch up, before it counts.
    /// </summary>
    private async Task CompareAsync(string pathAndQuery, int account)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var (main, service) = (await GetAsync(Main, pathAndQuery, account), await GetAsync(Service, pathAndQuery, account));
                if (main == service)
                {
                    Interlocked.Increment(ref _compared);
                    return;
                }
                if (attempt == 1)
                {
                    Interlocked.Increment(ref _compared);
                    Interlocked.Increment(ref _mismatched);
                    if (_samples.Count < 20) _samples.Enqueue($"{pathAndQuery}: main {main} / service {service}");
                    return;
                }
                await Task.Delay(1500); // longer than the feed's gap timeout plus the follower's interval
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // The shadow read failed; the customer's read already succeeded. Nothing to compare.
        }
    }
    // #endregion shadow

    private async Task<string> GetAsync(string destination, string pathAndQuery, int account)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Address(destination), pathAndQuery));
        request.Headers.Add("X-Customer-Account", account.ToString(CultureInfo.InvariantCulture));
        using var response = await clients.CreateClient(nameof(PayeesMigration)).SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    private Uri Address(string destination) => new(config[$"ReverseProxy:Clusters:payees:Destinations:{destination}:Address"]!);

    // #region move-writes
    /// <summary>
    /// Moves the writes to the other store: pause them, let those in flight finish, wait until
    /// the new owner has applied everything the old one wrote, switch, and resume. Returns how
    /// long writes were paused.
    /// </summary>
    public async Task<object> MoveWritesAsync(string to, bool waitForCatchUp)
    {
        var from = WritesTo;
        if (from == to) return new { from, to, pausedMs = 0L };
        var timer = Stopwatch.StartNew();
        WritesPaused = true;
        try
        {
            while (Volatile.Read(ref _inFlightWrites) > 0) await Task.Delay(5);
            if (waitForCatchUp)
            {
                var http = clients.CreateClient(nameof(PayeesMigration));
                var head = (await http.GetFromJsonAsync<Head>(new Uri(Address(from), "/payee-changes/head")))!.Position;
                while (true)
                {
                    var following = await http.GetFromJsonAsync<List<Following>>(new Uri(Address(to), "/payees/following")) ?? [];
                    if (following.Any(f => new Uri(f.Source) == Address(from) && f.Position >= head)) break;
                    if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException($"{to} didn't catch up with {from}.");
                    await Task.Delay(20);
                }
            }
            WritesTo = to;
        }
        finally
        {
            WritesPaused = false;
        }
        return new { from, to, pausedMs = timer.ElapsedMilliseconds };
    }
    // #endregion move-writes

    private sealed record Head(long Position);
    private sealed record Following(string Source, long Position);
}

static class PayeesMigrationEndpoints
{
    public static void AddPayeesMigration(this IServiceCollection services)
    {
        services.AddSingleton<PayeesMigration>();
        services.AddHttpClient(nameof(PayeesMigration), http => http.Timeout = TimeSpan.FromSeconds(5));
    }

    // Lab controls. A real migration would put these behind an operator's authentication.
    public static void MapPayeesMigration(this IEndpointRouteBuilder app)
    {
        app.MapGet("/lab/payees-migration", (PayeesMigration migration) => migration.Status);
        app.MapPost("/lab/payees-migration", (MigrationSettings settings, PayeesMigration migration) =>
        {
            if (settings.ReadPercent is { } percent) migration.ReadPercent = percent;
            if (settings.Shadow is { } shadow) migration.Shadow = shadow;
            return migration.Status;
        });
        app.MapPost("/lab/payees-migration/writes", async (string to, bool? wait, PayeesMigration migration) =>
            Results.Ok(await migration.MoveWritesAsync(to, wait ?? true)));
    }

    private sealed record MigrationSettings(int? ReadPercent, bool? Shadow);
}
