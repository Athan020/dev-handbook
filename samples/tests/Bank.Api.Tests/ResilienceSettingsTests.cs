using System.Net;
using Bank.Api.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Polly.CircuitBreaker;

namespace Bank.Api.Tests;

/// <summary>
/// B4 stage 7: test the resilience settings themselves. With a fake clock, ten seconds of a failing
/// fraud service at normal traffic take milliseconds to simulate, so a setting that would stop the
/// breaker opening fails the build, not the next outage.
/// </summary>
public sealed class ResilienceSettingsTests
{
    // #region breaker-opens
    // Normal traffic is 80 transfers a second. If the fraud service fails every call, the breaker
    // must open within ten seconds. Returns when it opened, or null if it never did.
    private static async Task<TimeSpan?> SecondsUntilBreakerOpensAsync(Dictionary<string, string?> settings)
    {
        var fraud = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var time = new FakeTimeProvider();
        var pipeline = FraudGate.BuildPipeline(fraud, time);
        var started = time.GetUtcNow();

        for (var call = 0; call < 80 * 10; call++)
        {
            time.Advance(TimeSpan.FromMilliseconds(1000.0 / 80));
            try
            {
                using var _ = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            }
            catch (BrokenCircuitException)
            {
                return time.GetUtcNow() - started;
            }
        }
        return null;
    }
    // #endregion breaker-opens

    [Fact]
    public async Task With_the_labs_settings_the_breaker_opens_within_a_second()
    {
        var opened = await SecondsUntilBreakerOpensAsync(new()
        {
            ["Breaker"] = "true", ["BreakerFailureRatio"] = "0.5", ["BreakerMinimumThroughput"] = "20", ["BreakerSamplingSeconds"] = "10",
        });

        Assert.NotNull(opened);
        Assert.True(opened < TimeSpan.FromSeconds(1), $"opened after {opened}");
    }

    [Fact]
    public async Task A_minimum_throughput_copied_from_a_busier_service_means_the_breaker_can_never_open()
    {
        // 80 calls a second for 10 seconds is 800 calls: the window can never hold 1,000.
        var opened = await SecondsUntilBreakerOpensAsync(new()
        {
            ["Breaker"] = "true", ["BreakerFailureRatio"] = "0.5", ["BreakerMinimumThroughput"] = "1000", ["BreakerSamplingSeconds"] = "10",
        });

        Assert.Null(opened);
    }
}
