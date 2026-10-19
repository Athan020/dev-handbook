using System.Net;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

/// <summary>B4 stage 6: a per-client rate limit refuses one client's flood without affecting others.</summary>
public sealed class RateLimitTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(RateLimits.ClientHeader, clientId);
        return client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task A_flooding_client_is_refused_and_another_client_is_not()
    {
        await using var api = fixture.App.WithWebHostBuilder(host => host
            .UseSetting("Lab:RateLimit", "PerClient")
            .UseSetting("Lab:RateLimitPerSecond", "5")
            .UseSetting("Lab:RateLimitBurst", "5"));
        var client = api.CreateClient();

        var flood = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => CallAsync(client, "partner")));
        var customer = await CallAsync(client, "customer-1");

        var refused = flood.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.InRange(refused.Count, 20, 25); // a burst of 5, plus whatever refilled meanwhile
        Assert.All(refused, r => Assert.True(r.Headers.RetryAfter is not null));
        Assert.Equal(HttpStatusCode.OK, customer.StatusCode);
    }
}
