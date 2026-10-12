using System.Net;
using System.Net.Http.Json;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

/// <summary>B1 stage 5: the summary's three versions agree, and writes invalidate the cache.</summary>
public sealed class SummaryTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string version, int cacheSeconds = 30) => fixture.App
        .WithWebHostBuilder(host => host
            .UseSetting("Lab:Summary", version)
            .UseSetting("Lab:CacheSeconds", cacheSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        .CreateClient();

    [Theory]
    [InlineData("CacheAside")]
    [InlineData("Hybrid")]
    public async Task Cached_versions_return_what_the_uncached_version_computes(string version)
    {
        var expected = await Client("None").GetFromJsonAsync<AccountSummary>("/accounts/1/summary", Ct);
        var cached = await Client(version).GetFromJsonAsync<AccountSummary>("/accounts/1/summary", Ct);

        Assert.NotNull(expected);
        Assert.NotNull(cached);
        Assert.Equal(expected.Balance, cached.Balance);
        Assert.Equal(expected.TransactionCount, cached.TransactionCount);
        Assert.Equal(expected.LastTransactionAt, cached.LastTransactionAt);
    }

    [Theory]
    [InlineData("CacheAside", 10)]
    [InlineData("Hybrid", 11)]
    public async Task Recording_a_transaction_invalidates_the_cached_summary(string version, int account)
    {
        var client = Client(version);
        var before = await client.GetFromJsonAsync<AccountSummary>($"/accounts/{account}/summary", Ct);
        Assert.NotNull(before);

        var response = await client.PostAsJsonAsync(
            $"/accounts/{account}/transactions", new NewTransaction(-25.50m, "Card payment"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var after = await client.GetFromJsonAsync<AccountSummary>($"/accounts/{account}/summary", Ct);
        Assert.NotNull(after);
        Assert.Equal(before.TransactionCount + 1, after.TransactionCount);
        Assert.Equal(before.Balance - 25.50m, after.Balance);
    }

    [Theory]
    [InlineData(0, "Card payment")]
    [InlineData(10, "")]
    public async Task Invalid_transactions_are_rejected(decimal amount, string description)
    {
        var response = await Client("None").PostAsJsonAsync(
            "/accounts/1/transactions", new NewTransaction(amount, description), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_accounts_return_404()
    {
        var client = Client("Hybrid");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/accounts/999999/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            "/accounts/999999/transactions", new NewTransaction(1m, "Salary"), Ct)).StatusCode);
    }
}
