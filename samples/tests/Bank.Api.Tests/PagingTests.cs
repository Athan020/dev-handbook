using System.Net;
using System.Net.Http.Json;
using Bank.Api.Data;
using Bank.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

/// <summary>
/// B1 stage 3 replaces the naive transactions query and adds keyset paging. These tests
/// check the replacements return the same data, and pin down the paging behaviour the
/// stage describes.
/// </summary>
public sealed class PagingTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string version) => fixture.App
        .WithWebHostBuilder(host => host.UseSetting("Lab:Transactions", version))
        .CreateClient();

    [Theory]
    [InlineData(1, 1, 20)]
    [InlineData(1, 3, 50)]
    [InlineData(7, 1, 100)]
    public async Task Projected_version_returns_exactly_what_the_naive_version_does(int account, int page, int pageSize)
    {
        var url = $"/accounts/{account}/transactions?page={page}&pageSize={pageSize}";

        var naive = await Client("Naive").GetFromJsonAsync<TransactionPage>(url, Ct);
        var projected = await Client("Projected").GetFromJsonAsync<TransactionPage>(url, Ct);

        Assert.NotNull(naive);
        Assert.NotNull(projected);
        Assert.NotEmpty(naive.Items);
        Assert.Equal(naive.Items, projected.Items);
    }

    [Fact]
    public async Task Blocking_version_returns_the_same_data_it_is_only_wrong_under_load()
    {
        const string url = "/accounts/1/transactions?pageSize=20";

        var blocking = await Client("Blocking").GetFromJsonAsync<TransactionPage>(url, Ct);
        var projected = await Client("Projected").GetFromJsonAsync<TransactionPage>(url, Ct);

        Assert.NotNull(blocking);
        Assert.NotNull(projected);
        Assert.Equal(projected.Items, blocking.Items);
    }

    [Fact]
    public async Task Projected_version_returns_404_for_an_unknown_account_and_empty_beyond_the_last_page()
    {
        var client = Client("Projected");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/accounts/999999/transactions", Ct)).StatusCode);

        var beyond = await client.GetFromJsonAsync<TransactionPage>("/accounts/1/transactions?page=100000", Ct);
        Assert.NotNull(beyond);
        Assert.Empty(beyond.Items);
    }

    [Fact]
    public async Task Feed_walks_every_transaction_newest_first_without_gaps_or_repeats()
    {
        var client = fixture.App.CreateClient();
        var seen = await ReadWholeFeedAsync(client, account: 2, pageSize: 7);

        var expected = await AllTransactionIdsNewestFirstAsync(account: 2);
        Assert.Equal(expected, seen.Select(t => t.Id));
    }

    [Fact]
    public async Task New_transactions_shift_offset_pages_but_not_the_feed()
    {
        // Account 5 is used only by this test, because it adds a transaction.
        const int account = 5;
        var client = Client("Projected");

        var offsetPage1 = await client.GetFromJsonAsync<TransactionPage>($"/accounts/{account}/transactions?page=1&pageSize=5", Ct);
        var feedPage1 = await client.GetFromJsonAsync<TransactionFeed>($"/accounts/{account}/transactions/feed?pageSize=5", Ct);
        Assert.NotNull(offsetPage1);
        Assert.NotNull(feedPage1);

        await AddTransactionAsync(account);

        var offsetPage2 = await client.GetFromJsonAsync<TransactionPage>($"/accounts/{account}/transactions?page=2&pageSize=5", Ct);
        var feedPage2 = await client.GetFromJsonAsync<TransactionFeed>(
            $"/accounts/{account}/transactions/feed?pageSize=5&cursor={feedPage1.NextCursor}", Ct);
        Assert.NotNull(offsetPage2);
        Assert.NotNull(feedPage2);

        // The new row pushed everything down by one, so offset page 2 repeats page 1's last row.
        Assert.Equal(offsetPage1.Items[^1].Id, offsetPage2.Items[0].Id);
        // The feed continues from where the client was.
        Assert.Empty(feedPage1.Items.Select(t => t.Id).Intersect(feedPage2.Items.Select(t => t.Id)));
        Assert.True(feedPage2.Items[0].CreatedAt <= feedPage1.Items[^1].CreatedAt);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("AAAA")]
    public async Task Feed_rejects_a_cursor_it_did_not_issue(string cursor)
    {
        var response = await fixture.App.CreateClient().GetAsync($"/accounts/1/transactions/feed?cursor={cursor}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Feed_returns_404_for_an_unknown_account()
    {
        var response = await fixture.App.CreateClient().GetAsync("/accounts/999999/transactions/feed", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<List<TransactionResponse>> ReadWholeFeedAsync(HttpClient client, int account, int pageSize)
    {
        var all = new List<TransactionResponse>();
        string? cursor = null;
        do
        {
            var url = $"/accounts/{account}/transactions/feed?pageSize={pageSize}" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await client.GetFromJsonAsync<TransactionFeed>(url, Ct);
            Assert.NotNull(page);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return all;
    }

    private async Task<List<long>> AllTransactionIdsNewestFirstAsync(int account)
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        return db.Transactions
            .Where(t => t.AccountId == account)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => t.Id)
            .ToList();
    }

    private async Task AddTransactionAsync(int account)
    {
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        db.Transactions.Add(new Transaction
        {
            AccountId = account,
            Amount = -42.00m,
            Description = "Card payment",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync(Ct);
    }
}
