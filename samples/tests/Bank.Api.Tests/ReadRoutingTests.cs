using System.Net.Http.Json;
using Bank.Api.Data;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

/// <summary>B1 stage 6: which database serves each read, under each Lab:Reads policy.</summary>
public sealed class ReadRoutingTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string policy) => fixture.App
        .WithWebHostBuilder(host => host.UseSetting("Lab:Reads", policy))
        .CreateClient();

    private static async Task<string> ServedByAsync(HttpClient client, string url, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) request.Headers.Add(ReadRouting.TokenHeader, token);
        using var response = await client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues(ReadRouting.ServedByHeader).Single();
    }

    [Theory]
    [InlineData("Primary", "primary")]
    [InlineData("Replica", "replica")]
    [InlineData("ReadYourWrites", "replica")]
    public async Task Reads_without_a_token_follow_the_policy(string policy, string expected)
    {
        var client = Client(policy);

        Assert.Equal(expected, await ServedByAsync(client, "/accounts/1/summary"));
        Assert.Equal(expected, await ServedByAsync(client, "/accounts/1/transactions/feed"));
    }

    [Fact]
    public async Task A_write_returns_a_token_and_the_writers_next_read_waits_for_a_replica_that_has_it()
    {
        var client = Client("ReadYourWrites");

        var response = await client.PostAsJsonAsync("/accounts/12/transactions", new NewTransaction(-5m, "Airtime"), Ct);
        response.EnsureSuccessStatusCode();
        var token = response.Headers.GetValues(ReadRouting.TokenHeader).Single();
        Assert.Matches("^[0-9A-F]+/[0-9A-F]+$", token);

        // The test "replica" can't confirm it has replayed the write, so the read goes to the primary.
        Assert.Equal("primary", await ServedByAsync(client, "/accounts/12/summary", token));
    }

    [Fact]
    public async Task A_malformed_token_is_not_trusted()
    {
        Assert.Equal("primary", await ServedByAsync(Client("ReadYourWrites"), "/accounts/1/summary", "not-an-lsn"));
    }

    [Fact]
    public async Task The_replica_policy_ignores_tokens()
    {
        Assert.Equal("replica", await ServedByAsync(Client("Replica"), "/accounts/1/summary", "0/0"));
    }
}
