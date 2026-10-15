using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Bank.Api.Endpoints;

public enum FraudDecision
{
    Approved,
    Declined,
    Unavailable,
}

public sealed record FraudOutcome(FraudDecision Decision, string Detail);

/// <summary>
/// Journey B4. Every transfer asks the fraud service for a decision before it happens
/// (Lab:FraudGate). How the API copes when that service is slow, failing or down is set by the
/// Fraud section, so each stage can switch one mechanism on:
///   Lab:FraudGate=Off (the default) | InTransaction | BeforeTransaction  where the call is made
///   Fraud:TimeoutMs        a timeout for each attempt (0: none, so HttpClient's 100 s default)
///   Fraud:Retries, Fraud:RetryDelayMs, Fraud:Backoff=Constant|Exponential, Fraud:Jitter=true
///   Fraud:Breaker=true, Fraud:BreakerFailureRatio, Fraud:BreakerMinimumThroughput,
///   Fraud:BreakerSamplingSeconds, Fraud:BreakerBreakSeconds
///   Fraud:MaxConcurrent, Fraud:QueueLimit   a bulkhead: calls allowed at once, and waiting
/// </summary>
public static class FraudGate
{
    public static IServiceCollection AddFraudGate(this IServiceCollection services, IConfiguration config)
    {
        var fraud = config.GetSection("Fraud");
        services.AddSingleton<FraudStats>();
        services.AddHttpClient<FraudClient>(http =>
                http.BaseAddress = new Uri(config["FakeServices:Url"] ?? "http://localhost:5090"))
            .AddResilienceHandler("fraud", (pipeline, context) => Build(pipeline, fraud, context.ServiceProvider.GetRequiredService<FraudStats>()));
        return services;
    }

    // Outermost first: the bulkhead admits a call, retries repeat it, the breaker can refuse
    // it, and the timeout limits each attempt.
    private static void Build(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, IConfiguration fraud, FraudStats stats)
    {
        // #region bulkhead
        if (fraud.GetValue("MaxConcurrent", 0) is var limit and > 0)
        {
            pipeline.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = limit,
                QueueLimit = fraud.GetValue("QueueLimit", 0),
            });
        }
        // #endregion bulkhead

        // #region retry
        if (fraud.GetValue("Retries", 0) is var retries and > 0)
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = retries,
                Delay = TimeSpan.FromMilliseconds(fraud.GetValue("RetryDelayMs", 200)),
                BackoffType = fraud["Backoff"] == "Exponential" ? DelayBackoffType.Exponential : DelayBackoffType.Constant,
                UseJitter = fraud.GetValue("Jitter", false),
                OnRetry = _ => { stats.Count("retry"); return default; },
            });
        }
        // #endregion retry

        // #region breaker
        if (fraud.GetValue("Breaker", false))
        {
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = fraud.GetValue("BreakerFailureRatio", 0.5),
                MinimumThroughput = fraud.GetValue("BreakerMinimumThroughput", 20),
                SamplingDuration = TimeSpan.FromSeconds(fraud.GetValue("BreakerSamplingSeconds", 10)),
                BreakDuration = TimeSpan.FromSeconds(fraud.GetValue("BreakerBreakSeconds", 5)),
                StateProvider = stats.Breaker,
                OnOpened = _ => { stats.Count("breaker opened"); return default; },
                OnClosed = _ => { stats.Count("breaker closed"); return default; },
            });
        }
        // #endregion breaker

        // #region timeout
        // A limit on each attempt. Polly cancels the attempt's cancellation token when it
        // expires, so this only works if everything below passes that token along.
        if (fraud.GetValue("TimeoutMs", 0) is var timeout and > 0)
        {
            pipeline.AddTimeout(TimeSpan.FromMilliseconds(timeout));
        }
        // #endregion timeout
    }
}

/// <summary>Counts what the fraud gate did, for GET /resilience/fraud.</summary>
public sealed class FraudStats
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();

    public CircuitBreakerStateProvider Breaker { get; } = new();

    public void Count(string what) => _counts.AddOrUpdate(what, 1, (_, n) => n + 1);

    public object Snapshot() => new
    {
        breaker = Breaker.CircuitState.ToString(),
        counts = _counts.OrderBy(c => c.Key).ToDictionary(c => c.Key, c => c.Value),
    };
}

/// <summary>The fraud service's API (Bank.FakeServices in the lab).</summary>
public sealed class FraudClient(HttpClient http, FraudStats stats)
{
    // #region decide
    // Every way of not getting an answer becomes Unavailable, with the reason: the caller
    // decides what that means for the transfer.
    public async Task<FraudOutcome> DecideAsync(int fromAccountId, int toAccountId, decimal amount, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/fraud/decision")
            {
                Content = JsonContent.Create(new { fromAccountId, toAccountId, amount }),
            };
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable($"status {(int)response.StatusCode}");
            }
            var body = await response.Content.ReadFromJsonAsync<DecisionBody>(ct);
            stats.Count(body?.Decision == "decline" ? "declined" : "approved");
            return new FraudOutcome(body?.Decision == "decline" ? FraudDecision.Declined : FraudDecision.Approved, $"{timer.ElapsedMilliseconds} ms");
        }
        catch (BrokenCircuitException)
        {
            return Unavailable("circuit open");
        }
        catch (TimeoutRejectedException)
        {
            return Unavailable("timed out");
        }
        catch (RateLimiterRejectedException)
        {
            return Unavailable("bulkhead full");
        }
        catch (HttpRequestException e)
        {
            return Unavailable(e.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unavailable("timed out (HttpClient's own timeout)");
        }
    }
    // #endregion decide

    private FraudOutcome Unavailable(string reason)
    {
        stats.Count($"unavailable: {reason}");
        return new FraudOutcome(FraudDecision.Unavailable, reason);
    }

    private sealed record DecisionBody(string Decision);
}
