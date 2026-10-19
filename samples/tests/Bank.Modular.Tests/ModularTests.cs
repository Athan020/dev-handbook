using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Bank.Modular.Tests;

public class ModularTests(ModularFixture fixture) : IClassFixture<ModularFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Paying_a_payee_moves_the_money_between_the_two_accounts()
    {
        using var http = fixture.App.CreateClient();
        var payee = await http.FirstPayeeAsync(10);
        var (from, to) = (await http.BalanceAsync(10), await http.BalanceAsync(payee.PayeeAccountId));

        var response = await http.PayAsync(10, payee.PayeeId, 12.34m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(from - 12.34m, await http.BalanceAsync(10));
        Assert.Equal(to + 12.34m, await http.BalanceAsync(payee.PayeeAccountId));
    }

    [Fact]
    public async Task Paying_someone_who_isnt_your_payee_is_refused()
    {
        using var http = fixture.App.CreateClient();
        var someoneElses = await http.FirstPayeeAsync(20);

        var response = await http.PayAsync(21, someoneElses.PayeeId, 1m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Paying_more_than_the_balance_is_refused_and_moves_nothing()
    {
        using var http = fixture.App.CreateClient();
        var payee = await http.FirstPayeeAsync(30);
        var before = await http.BalanceAsync(30);

        var response = await http.PayAsync(30, payee.PayeeId, before + 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(before, await http.BalanceAsync(30));
    }

    [Fact]
    public async Task A_payee_can_be_saved_once()
    {
        using var http = fixture.App.CreateClient();

        var first = await http.PostAsJsonAsync("/accounts/40/payees", new { payeeAccountId = 1, name = "  Landlord ", reference = "Rent" }, Ct);
        var again = await http.PostAsJsonAsync("/accounts/40/payees", new { payeeAccountId = 1, name = "Landlord" }, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("Landlord", (await first.Content.ReadFromJsonAsync<PayeeDto>(Ct))!.Name);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task The_statement_shows_money_in_and_out()
    {
        using var http = fixture.App.CreateClient();
        var payee = await http.FirstPayeeAsync(45);
        await http.PayAsync(45, payee.PayeeId, 7m);
        var now = DateTimeOffset.UtcNow;

        var payer = await http.GetFromJsonAsync<StatementDto>($"/accounts/45/statements/{now.Year}/{now.Month}", Ct);
        var payeeStatement = await http.GetFromJsonAsync<StatementDto>($"/accounts/{payee.PayeeAccountId}/statements/{now.Year}/{now.Month}", Ct);

        Assert.True(payer!.MoneyOut >= 7m);
        Assert.True(payeeStatement!.MoneyIn >= 7m);
    }

    // #region feed-gap-test
    [Fact]
    public async Task The_feed_waits_for_an_entry_that_is_still_being_committed()
    {
        using var http = fixture.App.CreateClient();
        var start = await LastPositionAsync(http);

        // A transfer that has inserted its entry, taking the next position, but not committed.
        await using var slow = new NpgsqlConnection(fixture.ConnectionString);
        await slow.OpenAsync(Ct);
        await using var transaction = await slow.BeginTransactionAsync(Ct);
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO ledger.entries (transfer_id, account_id, amount, reference, posted_at) VALUES (gen_random_uuid(), 1, 0, 'slow', now())",
            slow, transaction))
        {
            await insert.ExecuteNonQueryAsync(Ct);
        }

        // Meanwhile a later transfer commits, with later positions.
        var payee = await http.FirstPayeeAsync(2);
        await http.PayAsync(2, payee.PayeeId, 1m);

        Assert.Empty(await http.GetFromJsonAsync<List<EntryDto>>($"/ledger/feed?after={start}", Ct) ?? []);

        await transaction.CommitAsync(Ct);
        var feed = await http.GetFromJsonAsync<List<EntryDto>>($"/ledger/feed?after={start}", Ct);
        Assert.Equal(["slow", "Payment", "Payment"], feed!.Select(e => e.Reference));
    }
    // #endregion feed-gap-test

    private static async Task<long> LastPositionAsync(HttpClient http)
    {
        long position = 0;
        while (await http.GetFromJsonAsync<List<EntryDto>>($"/ledger/feed?after={position}&max=5000", Ct) is { Count: > 0 } page)
            position = page[^1].Position;
        return position;
    }
}
