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

    private sealed record ReviewCount(string Status, int Count, decimal Amount);

    private static async Task<int> ReviewsAsync(HttpClient client, string status) =>
        (await client.GetFromJsonAsync<List<ReviewCount>>("/fraud-reviews/summary", Ct))!.FirstOrDefault(r => r.Status == status)?.Count ?? 0;

    [Fact]
    public async Task Failing_closed_refuses_and_failing_open_goes_ahead_but_records_it()
    {
        var down = new StubFraud((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using (var closed = Api(down, ("Lab:FraudGate", "BeforeTransaction"), ("Fraud:Fallback", "FailClosed")))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await TransferAsync(closed.CreateClient())).StatusCode);
        }

        await using var open = Api(down, ("Lab:FraudGate", "BeforeTransaction"), ("Fraud:Fallback", "FailOpen"));
        var client = open.CreateClient();
        var before = await ReviewsAsync(client, "Unchecked");
        Assert.Equal(HttpStatusCode.Created, (await TransferAsync(client, 50_000m)).StatusCode);
        Assert.Equal(before + 1, await ReviewsAsync(client, "Unchecked"));
    }

    [Fact]
    public async Task Allowing_small_transfers_holds_large_ones_until_fraud_can_decide()
    {
        var up = false;
        var fraud = new StubFraud((_, _) => Task.FromResult(up ? Decision("approve") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using var api = Api(fraud, ("Lab:FraudGate", "BeforeTransaction"), ("Fraud:Fallback", "AllowSmall"), ("Fraud:FallbackLimit", "1000"));
        var client = api.CreateClient();
        var releasedBefore = await ReviewsAsync(client, "Released");

        Assert.Equal(HttpStatusCode.Created, (await TransferAsync(client, 200m)).StatusCode);
        var large = await TransferAsync(client, 5_000m);
        Assert.Equal(HttpStatusCode.Accepted, large.StatusCode);

        up = true; // the fraud service recovers; the releaser makes the held transfer
        for (var i = 0; i < 40 && await ReviewsAsync(client, "Released") == releasedBefore; i++)
        {
            await Task.Delay(250, Ct);
        }
        Assert.Equal(releasedBefore + 1, await ReviewsAsync(client, "Released"));
    }

    [Fact]
    public async Task An_open_breaker_fails_fast_without_calling_the_fraud_service()
    {
        var fraud = new StubFraud((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using var api = Api(fraud,
            ("Lab:FraudGate", "BeforeTransaction"), ("Fraud:Breaker", "true"),
            ("Fraud:BreakerMinimumThroughput", "5"), ("Fraud:BreakerBreakSeconds", "30"));
        var client = api.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await TransferAsync(client)).StatusCode);
        }
        var callsWhenOpened = fraud.Calls;
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await TransferAsync(client)).StatusCode);
        }

        Assert.Equal(5, callsWhenOpened);
        Assert.Equal(callsWhenOpened, fraud.Calls); // nothing reached the fraud service while open
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

/// <summary>B4 stage 2: the retry budget caps retries at a share of recent calls.</summary>
public sealed class RetryBudgetTests
{
    [Fact]
    public void Retries_stop_at_ten_plus_the_percentage_of_calls()
    {
        var budget = new RetryBudget(percent: 10);
        for (var i = 0; i < 1_000; i++)
        {
            budget.CountCall();
        }

        var allowed = Enumerable.Range(0, 1_000).Count(_ => budget.TryRetry());

        Assert.Equal(110, allowed); // 10 to spare, plus 10% of 1,000 calls
    }

    [Fact]
    public void With_no_calls_only_the_spare_retries_are_allowed()
    {
        var budget = new RetryBudget(percent: 10);
        Assert.Equal(10, Enumerable.Range(0, 100).Count(_ => budget.TryRetry()));
    }
}
