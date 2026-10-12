using System.Net.Http.Json;

namespace Bank.Messaging;

/// <summary>
/// The work that follows a transfer: emailing the customer, and asking the fraud service to
/// score it. Both are calls to other services (in the lab, Bank.FakeServices), so both can be
/// slow or fail. The API and the worker share this class, so every version of journey B2
/// does exactly the same work; only where and when it runs changes.
/// </summary>
public sealed class FollowUp(HttpClient http)
{
    public const string HttpClientName = "follow-up";

    public async Task RunAsync(TransferCompleted transfer, CancellationToken ct)
    {
        await SendEmailAsync(transfer, ct);
        await ScoreForFraudAsync(transfer, ct);
    }

    public async Task SendEmailAsync(TransferCompleted transfer, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/email", new
        {
            transfer.TransferId,
            To = $"account-{transfer.FromAccountId}@example.com",
            Subject = $"You sent R{transfer.Amount:N2}",
            transfer.CompletedAt,
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ScoreForFraudAsync(TransferCompleted transfer, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/fraud-check", transfer, ct);
        response.EnsureSuccessStatusCode();
    }
}
