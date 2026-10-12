using System.Net;
using System.Net.Http.Json;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

/// <summary>B3 stage 3: idempotency keys make a retried transfer safe.</summary>
public sealed class IdempotencyTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string mode) => fixture.App
        .WithWebHostBuilder(host => host
            .UseSetting("Lab:Transfers", "Background")
            .UseSetting("FakeServices:Url", "http://127.0.0.1:9")
            .UseSetting("Lab:Funds", "Conditional")
            .UseSetting("Lab:LockOrder", "AccountId")
            .UseSetting("Lab:Idempotency", mode))
        .CreateClient();

    private static async Task<int> OpenWithAsync(HttpClient client, decimal deposit)
    {
        var opened = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("Idempotency test"), Ct);
        var id = (await opened.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.Id;
        if (deposit > 0)
        {
            (await client.PostAsJsonAsync($"/accounts/{id}/transactions", new NewTransaction(deposit, "Deposit"), Ct)).EnsureSuccessStatusCode();
        }
        return id;
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, TransferRequest request, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/transfers") { Content = JsonContent.Create(request) };
        message.Headers.Add(Idempotency.Header, key);
        return client.SendAsync(message, Ct);
    }

    private static async Task<decimal> BalanceAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<AccountSummary>($"/accounts/{id}/summary", Ct))!.Balance;

    [Fact]
    public async Task A_retry_with_the_same_key_gets_the_same_answer_and_moves_no_more_money()
    {
        var client = Client("Keys");
        var from = await OpenWithAsync(client, 100m);
        var to = await OpenWithAsync(client, 0m);
        var request = new TransferRequest(from, to, 30m, "Rent");

        var first = await PayAsync(client, request, "pay-rent-1");
        var retry = await PayAsync(client, request, "pay-rent-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(retry.Headers.Contains(Idempotency.ReplayedHeader));
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<Bank.Messaging.TransferCompleted>(Ct))!.TransferId,
            (await retry.Content.ReadFromJsonAsync<Bank.Messaging.TransferCompleted>(Ct))!.TransferId);
        Assert.Equal(30m, await BalanceAsync(client, to));
    }

    [Fact]
    public async Task Concurrent_requests_with_one_key_move_the_money_once()
    {
        var client = Client("Keys");
        var from = await OpenWithAsync(client, 100m);
        var to = await OpenWithAsync(client, 0m);
        var request = new TransferRequest(from, to, 10m, "Double tap");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PayAsync(client, request, "double-tap")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(7, responses.Count(r => r.Headers.Contains(Idempotency.ReplayedHeader)));
        Assert.Equal(10m, await BalanceAsync(client, to));
    }

    [Fact]
    public async Task The_same_key_with_a_different_request_is_rejected()
    {
        var client = Client("Keys");
        var from = await OpenWithAsync(client, 100m);
        var to = await OpenWithAsync(client, 0m);

        await PayAsync(client, new TransferRequest(from, to, 10m, "Lunch"), "reused");
        var different = await PayAsync(client, new TransferRequest(from, to, 50m, "Lunch"), "reused");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Equal(10m, await BalanceAsync(client, to));
    }

    [Fact]
    public async Task A_refused_transfer_does_not_use_up_the_key()
    {
        var client = Client("Keys");
        var from = await OpenWithAsync(client, 5m);
        var to = await OpenWithAsync(client, 0m);
        var request = new TransferRequest(from, to, 20m, "Too much");

        var refused = await PayAsync(client, request, "after-payday");
        (await client.PostAsJsonAsync($"/accounts/{from}/transactions", new NewTransaction(100m, "Salary"), Ct)).EnsureSuccessStatusCode();
        var retried = await PayAsync(client, request, "after-payday");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }

    [Fact]
    public async Task Without_keys_a_retry_is_a_second_transfer()
    {
        var client = Client("None");
        var from = await OpenWithAsync(client, 100m);
        var to = await OpenWithAsync(client, 0m);
        var request = new TransferRequest(from, to, 10m, "Coffee");

        await PayAsync(client, request, "ignored");
        await PayAsync(client, request, "ignored");

        Assert.Equal(20m, await BalanceAsync(client, to));
    }
}
