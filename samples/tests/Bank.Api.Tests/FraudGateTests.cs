using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Bank.Api.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

/// <summary>B4: the fraud gate on transfers, with the fraud service slow, failing or down.</summary>
public sealed class FraudGateTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A stand-in fraud service: approves, declines, hangs or fails, and counts calls.</summary>
    private sealed class StubFraud(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            respond(Interlocked.Increment(ref _calls), ct);
    }

    private static HttpResponseMessage Decision(string decision) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { decision }) };

    private WebApplicationFactory<Program> Api(StubFraud fraud, params (string Key, string Value)[] settings) =>
        fixture.App.WithWebHostBuilder(host =>
        {
            host.UseSetting("Lab:Transfers", "Background").UseSetting("FakeServices:Url", "http://127.0.0.1:9");
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }
            host.ConfigureTestServices(services =>
                services.AddHttpClient<FraudClient>().ConfigurePrimaryHttpMessageHandler(() => fraud));
        });

    private static Task<HttpResponseMessage> TransferAsync(HttpClient client, decimal amount = 10m) =>
        client.PostAsJsonAsync("/transfers", new TransferRequest(30, 31, amount, "Fraud gate"), Ct);

    [Fact]
    public async Task An_approved_transfer_goes_through_and_a_declined_one_does_not()
    {
        var fraud = new StubFraud((n, _) => Task.FromResult(Decision(n == 1 ? "approve" : "decline")));
        await using var api = Api(fraud, ("Lab:FraudGate", "BeforeTransaction"));
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await TransferAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await TransferAsync(client)).StatusCode);
    }

    [Theory]
    [InlineData("BeforeTransaction")]
    [InlineData("InTransaction")]
    public async Task A_hanging_fraud_service_is_cut_off_by_the_timeout(string placement)
    {
        var fraud = new StubFraud(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return Decision("approve"); });
        await using var api = Api(fraud, ("Lab:FraudGate", placement), ("Fraud:TimeoutMs", "300"));
        var client = api.CreateClient();

        var timer = Stopwatch.StartNew();
        var response = await TransferAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"took {timer.Elapsed}");
    }
}
