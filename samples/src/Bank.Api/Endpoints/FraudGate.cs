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

public enum FallbackAction
{
    Proceed,
    ProceedUnchecked,
    Hold,
    Refuse,
}

/// <summary>
/// Journey B4. Every transfer asks the fraud service for a decision before it happens
/// (Lab:FraudGate). How the API copes when that service is slow, failing or down is set by the
/// Fraud section, so each stage can switch one mechanism on:
///   Lab:FraudGate=Off (the default) | InTransaction | BeforeTransaction  where the call is made
///   Fraud:TimeoutMs        a timeout for each attempt (0: none, so HttpClient's 100 s default)
///   Fraud:Retries, Fraud:RetryDelayMs, Fraud:Backoff=Constant|Exponential, Fraud:Jitter=true,
///   Fraud:RetryBudgetPercent (retries allowed as a percentage of recent calls)
///   Fraud:Breaker=true, Fraud:BreakerFailureRatio, Fraud:BreakerMinimumThroughput,
///   Fraud:BreakerSamplingSeconds, Fraud:BreakerBreakSeconds
///   Fraud:MaxConcurrent, Fraud:QueueLimit   a bulkhead: calls allowed at once, and waiting
///   Fraud:Fallback=FailClosed (the default) | FailOpen | AllowSmall, with Fraud:FallbackLimit
///     what to do when there's no decision (stage 5; with Lab:FraudGate=BeforeTransaction)
/// </summary>
public static class FraudGate
{
    // #region fallback
    // What a transfer does with the fraud service's answer, or without one. The fallback is a
    // business decision about risk, not a technical default.
    public static FallbackAction OnOutcome(FraudOutcome outcome, decimal amount, IConfiguration config) => outcome.Decision switch
    {
        FraudDecision.Approved => FallbackAction.Proceed,
        FraudDecision.Declined => FallbackAction.Refuse,
        _ => config["Fraud:Fallback"] switch
        {
            // Every transfer goes ahead unchecked: no outage for customers, unlimited fraud risk.
            "FailOpen" => FallbackAction.ProceedUnchecked,
            // Small transfers go ahead, recorded for review afterwards; large ones wait for a decision.
            "AllowSmall" => amount <= config.GetValue("Fraud:FallbackLimit", 1_000m) ? FallbackAction.ProceedUnchecked : FallbackAction.Hold,
            // No transfer without a decision: no fraud risk, a full outage for customers.
            _ => FallbackAction.Refuse,
        },
    };
    // #endregion fallback

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
            var budget = fraud.GetValue("RetryBudgetPercent", 0) is var percent and > 0 ? new RetryBudget(percent) : null;
            if (budget is not null)
            {
                pipeline.AddStrategy(_ => new CountCalls(budget), new CountCallsOptions()); // outside the retries: once per call
            }
            var transient = new HttpRetryStrategyOptions().ShouldHandle; // timeouts, 5xx, 429, connection errors
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = retries,
                Delay = TimeSpan.FromMilliseconds(fraud.GetValue("RetryDelayMs", 200)),
                BackoffType = fraud["Backoff"] == "Exponential" ? DelayBackoffType.Exponential : DelayBackoffType.Constant,
                UseJitter = fraud.GetValue("Jitter", false),
                // Retry only transient failures, and only while retries are a small share of calls.
                ShouldHandle = async args => await transient(args) && (budget?.TryRetry() ?? true),
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

// #region retry-budget
/// <summary>
/// B4 stage 2: retries are allowed only while they're at most a set percentage of recent calls
/// (over the last ten seconds), plus a few to spare. When a dependency is failing everything,
/// retries can't multiply the load: they're capped at a fraction of it.
/// </summary>
public sealed class RetryBudget(int percent)
{
    private readonly object _gate = new();
    private readonly Queue<(DateTimeOffset At, bool Retry)> _recent = new();

    public void CountCall() => Add(retry: false);

    public bool TryRetry()
    {
        lock (_gate)
        {
            Trim();
            var calls = _recent.Count(r => !r.Retry);
            var retries = _recent.Count(r => r.Retry);
            if (retries >= 10 + calls * percent / 100)
            {
                return false;
            }
            _recent.Enqueue((DateTimeOffset.UtcNow, true));
            return true;
        }
    }

    private void Add(bool retry)
    {
        lock (_gate)
        {
            Trim();
            _recent.Enqueue((DateTimeOffset.UtcNow, retry));
        }
    }

    private void Trim()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-10);
        while (_recent.Count > 0 && _recent.Peek().At < cutoff)
        {
            _recent.Dequeue();
        }
    }
}
// #endregion retry-budget

/// <summary>Counts each original call (not each retry) towards the retry budget.</summary>
internal sealed class CountCalls(RetryBudget budget) : ResilienceStrategy
{
    protected override ValueTask<Outcome<TResult>> ExecuteCore<TResult, TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback, ResilienceContext context, TState state)
    {
        budget.CountCall();
        return callback(context, state);
    }
}

internal sealed class CountCallsOptions : ResilienceStrategyOptions;
