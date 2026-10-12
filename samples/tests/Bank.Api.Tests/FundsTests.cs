using System.Net;
using System.Net.Http.Json;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

/// <summary>B3 stage 1: a transfer the account can't cover is refused, even when transfers race.</summary>
public sealed class FundsTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string isolation, int retries = 0) => fixture.App
        .WithWebHostBuilder(host => host
            .UseSetting("Lab:Transfers", "Background")
            .UseSetting("FakeServices:Url", "http://127.0.0.1:9")
            .UseSetting("Lab:Funds", "CheckSum")
            .UseSetting("Lab:Isolation", isolation)
            .UseSetting("Lab:Retries", retries.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        .CreateClient();

    private static async Task<int> OpenWithAsync(HttpClient client, decimal deposit)
    {
        var opened = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("Funds test"), Ct);
        var id = (await opened.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.Id;
        if (deposit > 0)
        {
            (await client.PostAsJsonAsync($"/accounts/{id}/transactions", new NewTransaction(deposit, "Deposit"), Ct)).EnsureSuccessStatusCode();
        }
        return id;
    }

    private static async Task<decimal> BalanceAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<AccountSummary>($"/accounts/{id}/summary", Ct))!.Balance;

    [Fact]
    public async Task A_transfer_the_account_cannot_cover_is_refused()
    {
        var client = Client("ReadCommitted");
        var from = await OpenWithAsync(client, 100m);
        var to = await OpenWithAsync(client, 0m);

        var first = await client.PostAsJsonAsync("/transfers", new TransferRequest(from, to, 60m, "Rent"), Ct);
        var second = await client.PostAsJsonAsync("/transfers", new TransferRequest(from, to, 60m, "Rent"), Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(40m, await BalanceAsync(client, from));
    }

    [Fact]
    public async Task Serializable_with_retries_never_overdraws_when_transfers_race()
    {
        var client = Client("Serializable", retries: 10);
        for (var round = 0; round < 3; round++)
        {
            var from = await OpenWithAsync(client, 100m);
            var to = await OpenWithAsync(client, 0m);

            var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
                client.PostAsJsonAsync("/transfers", new TransferRequest(from, to, 60m, "Race"), Ct)));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
            Assert.Equal(40m, await BalanceAsync(client, from));
        }
    }
}
