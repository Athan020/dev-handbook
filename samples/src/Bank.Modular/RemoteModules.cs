using System.Globalization;
using System.Net;
using Bank.Modular.Contracts.Ledger;
using Bank.Modular.Contracts.Payees;

// #region remote-modules
/// <summary>
/// B5 stage 3 onwards: which modules this process runs. Modules:Run lists them (default: all).
/// For a module that runs elsewhere, Modules:{Name}Url gives its address, and other modules
/// reach it through the same contract, now over HTTP. Same code, deployed as separate processes.
/// </summary>
sealed record ModuleLayout(bool Ledger, bool Payees, bool Statements)
{
    public static ModuleLayout From(IConfiguration config)
    {
        var run = (config["Modules:Run"] ?? "Ledger,Payees,Statements")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ModuleLayout(run.Contains("Ledger"), run.Contains("Payees"), run.Contains("Statements"));
    }
}

static class RemoteModules
{
    public static void AddRemoteLedger(this IServiceCollection services, IConfiguration config) =>
        services.AddHttpClient<ILedger, LedgerClient>(http => Configure(http, config, "Ledger"));

    public static void AddRemotePayees(this IServiceCollection services, IConfiguration config) =>
        services.AddHttpClient<IPayees, PayeesClient>(http => Configure(http, config, "Payees"));

    private static void Configure(HttpClient http, IConfiguration config, string module)
    {
        http.BaseAddress = new Uri(config[$"Modules:{module}Url"]
            ?? throw new InvalidOperationException($"The {module} module isn't run here, so Modules:{module}Url must say where it is."));
        // A call that used to be a function call can now hang (B4 stage 1).
        http.Timeout = TimeSpan.FromMilliseconds(config.GetValue("Modules:TimeoutMs", 2000));
    }
}
// #endregion remote-modules

/// <summary>The ledger's contract, over HTTP, for modules running in another process.</summary>
sealed class LedgerClient(HttpClient http) : ILedger
{
    public async Task<AccountView?> GetAccountAsync(int accountId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/accounts/{accountId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountView>(ct);
    }

    public async Task<IReadOnlyList<LedgerEntry>> EntriesAsync(int accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<LedgerEntry>>(
            $"/accounts/{accountId}/entries?from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}&to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}", ct) ?? [];

    public async Task<IReadOnlyList<LedgerEntry>> ReadFeedAsync(long position, int max, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<LedgerEntry>>($"/ledger/feed?after={position}&max={max}", ct) ?? [];
}

/// <summary>The payees module's contract, over HTTP.</summary>
sealed class PayeesClient(HttpClient http) : IPayees
{
    public async Task<Payee?> GetAsync(int customerAccountId, Guid payeeId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/accounts/{customerAccountId}/payees/{payeeId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Payee>(ct);
    }
}

/// <summary>
/// A module in another process can be down or slow. Answer 503, saying so, rather than a 500
/// that looks like a bug.
/// </summary>
sealed class UnavailableModuleHandler(IProblemDetailsService problems) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not (HttpRequestException or TaskCanceledException { InnerException: TimeoutException })) return false;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = 503, Title = "A module this request needs is unavailable. Try again shortly." },
        });
    }
}
