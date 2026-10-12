using System.Net;
using System.Net.Http.Json;
using Bank.Api.Data;
using Bank.Api.Endpoints;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

/// <summary>B2: a transfer records a debit and a credit together, whatever happens afterwards.</summary>
public sealed class TransferTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Background mode responds without waiting for follow-up work. Port 9 refuses connections,
    // so the follow-up fails quickly and quietly instead of reaching real services.
    private HttpClient Client(string mode = "Background") => fixture.App
        .WithWebHostBuilder(host => host
            .UseSetting("Lab:Transfers", mode)
            .UseSetting("FakeServices:Url", "http://127.0.0.1:9"))
        .CreateClient();

    [Fact]
    public async Task A_transfer_records_a_debit_and_a_matching_credit()
    {
        var response = await Client().PostAsJsonAsync("/transfers", new TransferRequest(20, 21, 125.50m, "Rent"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.Content.ReadFromJsonAsync<TransferCompleted>(Ct);
        Assert.NotNull(transfer);

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        var rows = await db.Transactions.Where(t => t.CreatedAt == transfer.CompletedAt).ToListAsync(Ct);
        Assert.Contains(rows, t => t.AccountId == 20 && t.Amount == -125.50m && t.CounterpartyAccountId == 21);
        Assert.Contains(rows, t => t.AccountId == 21 && t.Amount == 125.50m && t.CounterpartyAccountId == 20);
    }

    [Fact]
    public async Task Inline_follow_up_failing_fails_the_request_even_though_the_money_moved()
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        var before = await db.Transactions.CountAsync(t => t.AccountId == 22, Ct);

        var response = await Client("Inline").PostAsJsonAsync("/transfers", new TransferRequest(22, 23, 10m, "Groceries"), Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before + 1, await db.Transactions.CountAsync(t => t.AccountId == 22, Ct));
    }

    [Theory]
    [InlineData(1, 1, 10)]   // same account
    [InlineData(1, 2, 0)]    // no amount
    [InlineData(1, 2, -5)]   // negative amount
    public async Task Invalid_transfers_are_rejected(int from, int to, decimal amount)
    {
        var response = await Client().PostAsJsonAsync("/transfers", new TransferRequest(from, to, amount, "Test"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfers_involving_an_unknown_account_return_404()
    {
        var response = await Client().PostAsJsonAsync("/transfers", new TransferRequest(1, 999_999, 10m, "Test"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
