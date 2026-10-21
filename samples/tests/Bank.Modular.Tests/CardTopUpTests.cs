using System.Net;
using System.Net.Http.Json;

namespace Bank.Modular.Tests;

public class CardTopUpTests(ModularFixture fixture) : IClassFixture<ModularFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record TopUp(Guid TopUpId, string Status, string? Detail);

    [Fact]
    public async Task A_compensated_top_up_returns_the_money_and_keeps_the_books_right()
    {
        // No cards service is listening, so the credit fails, and the in-memory version refunds:
        // two moves of money in one request.
        await using var app = fixture.App.WithWebHostBuilder(host => host
            .UseSetting("Lab:TopUps", "InMemory")
            .UseSetting("FakeServices:Url", "http://127.0.0.1:9"));
        using var http = app.CreateClient();
        var before = await http.BalanceAsync(41);

        var response = await http.PostAsJsonAsync("/card-top-ups", new { accountId = 41, cardId = "card-41", amount = 25m }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(before, await http.BalanceAsync(41));
        Assert.Equal(0m, await http.BalanceAsync(-1)); // nothing left in transit
        var books = await http.GetFromJsonAsync<BooksDto>("/ledger/check", Ct);
        Assert.Equal(0, books!.AccountsDisagreeing);
    }

    [Fact]
    public async Task The_saga_records_the_debit_and_keeps_the_top_up_until_the_cards_service_answers()
    {
        await using var app = fixture.App.WithWebHostBuilder(host => host
            .UseSetting("Lab:TopUps", "Saga")
            .UseSetting("FakeServices:Url", "http://127.0.0.1:9"));
        using var http = app.CreateClient();
        var before = await http.BalanceAsync(42);

        var response = await http.PostAsJsonAsync("/card-top-ups", new { accountId = 42, cardId = "card-42", amount = 30m }, Ct);
        var accepted = await response.Content.ReadFromJsonAsync<TopUp>(Ct);
        await Task.Delay(500, Ct); // the orchestrator tries, and gets no answer

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before - 30m, await http.BalanceAsync(42));
        var topUp = await http.GetFromJsonAsync<TopUp>($"/card-top-ups/{accepted!.TopUpId}", Ct);
        Assert.Equal("Debited", topUp!.Status); // not refunded: no answer isn't a refusal
    }
}
