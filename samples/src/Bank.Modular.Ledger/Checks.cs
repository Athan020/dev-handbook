using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace Bank.Modular.Ledger;

/// <summary>
/// B5 stage 4: the services a transfer must hear from before money moves, such as customers
/// (is the account in good standing?), limits and fraud. Transfers:Checks lists them; in the
/// lab they're Bank.FakeServices' /checks/{name}. Lab:Checks=Sequential|Parallel decides how
/// they're called, and Transfers:CheckRetries how many times a failed check is retried.
/// </summary>
internal sealed record ChecksOptions(string[] Names, bool Parallel, int Retries)
{
    public static ChecksOptions From(IConfiguration config) => new(
        (config["Transfers:Checks"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        config["Lab:Checks"] == "Parallel",
        config.GetValue("Transfers:CheckRetries", 0));
}

internal sealed class TransferChecks(HttpClient http, ChecksOptions options)
{
    // #region run-checks
    /// <summary>Runs every check; throws if any of them can't give an answer.</summary>
    public async Task RunAsync(int fromAccountId, int toAccountId, decimal amount, CancellationToken ct)
    {
        if (options.Parallel)
        {
            // All at once: the transfer waits for the slowest check, not the sum of them all.
            await Task.WhenAll(options.Names.Select(name => CheckAsync(name, fromAccountId, toAccountId, amount, ct)));
        }
        else
        {
            foreach (var name in options.Names)
                await CheckAsync(name, fromAccountId, toAccountId, amount, ct);
        }
    }

    private async Task CheckAsync(string name, int fromAccountId, int toAccountId, decimal amount, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await http.PostAsJsonAsync($"/checks/{name}", new { fromAccountId, toAccountId, amount }, ct);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException) when (attempt < options.Retries)
            {
                // A check is a question, so asking again is safe (B4 stage 2).
            }
        }
    }
    // #endregion run-checks
}
