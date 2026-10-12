using System.Net;
using System.Net.Http.Json;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

public sealed class AccountEndpointsTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private readonly HttpClient _client = fixture.App.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_account_returns_the_account()
    {
        var account = await _client.GetFromJsonAsync<AccountResponse>("/accounts/1", Ct);

        Assert.NotNull(account);
        Assert.Equal(1, account.Id);
        Assert.Equal("ZAR", account.Currency);
        Assert.False(string.IsNullOrWhiteSpace(account.HolderName));
    }

    [Fact]
    public async Task Get_unknown_account_returns_404()
    {
        var response = await _client.GetAsync("/accounts/999999", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transactions_come_newest_first_one_page_at_a_time()
    {
        var first = await _client.GetFromJsonAsync<TransactionPage>("/accounts/1/transactions?pageSize=10", Ct);
        var second = await _client.GetFromJsonAsync<TransactionPage>("/accounts/1/transactions?page=2&pageSize=10", Ct);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(10, second.Items.Count);

        var both = first.Items.Concat(second.Items).ToList();
        Assert.Equal(both.OrderByDescending(t => t.CreatedAt).Select(t => t.Id), both.Select(t => t.Id));
        Assert.Empty(first.Items.Select(t => t.Id).Intersect(second.Items.Select(t => t.Id)));
    }

    [Fact]
    public async Task Transfers_have_a_counterparty_name_and_other_transactions_do_not()
    {
        var page = await _client.GetFromJsonAsync<TransactionPage>(
            $"/accounts/1/transactions?pageSize={AccountEndpoints.MaxPageSize}", Ct);

        Assert.NotNull(page);
        var transfers = page.Items.Where(t => t.Description is "Transfer" or "Incoming transfer").ToList();
        Assert.NotEmpty(transfers);
        Assert.All(transfers, t => Assert.False(string.IsNullOrWhiteSpace(t.CounterpartyName)));
        Assert.All(page.Items.Except(transfers), t => Assert.Null(t.CounterpartyName));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Invalid_paging_returns_400(string query)
    {
        var response = await _client.GetAsync($"/accounts/1/transactions?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transactions_for_an_unknown_account_return_404()
    {
        var response = await _client.GetAsync("/accounts/999999/transactions", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
