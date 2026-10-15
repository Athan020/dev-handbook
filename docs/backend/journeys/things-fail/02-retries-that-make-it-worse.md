---
title: Retries that make it worse
track: backend
journey: things-fail
stage: 2
level: intermediate
sources: [02-system-design/redundancy-fault-tolerance.md]
status: written
---

# 2. Retries that make it worse

## Where we are

Stage 1's timeouts turned hangs into quick failures. Then the fraud service has a three-second slow spell, every transfer in those seconds fails, and support hears about it. The obvious fix goes in: if the fraud call fails, try again, three times.

The next slow spell lasts three seconds again. The outage that follows lasts until someone restarts things by hand.

## Stop and think

::: think The slow spell lasted three seconds. Why did the outage go on after it ended?

Because the retries kept the fraud service overloaded. During the slow spell, calls time out, and each one is retried, up to three times: up to four calls for every transfer. The fraud service can't tell that a caller has given up, so it still works through every queued call, including the abandoned ones. When it speeds up again, it's facing four times the normal load plus a backlog, so calls keep timing out, and keep being retried.

In the lab, with the fraud service running at three-quarters of its capacity, a three-second slow spell with no retries caused about ten seconds of failures. With three retries, no transfer succeeded for the remaining 35 seconds of the run, while the fraud service received 600 calls a second for 150 transfers a second. This is a *metastable failure*: the trigger is gone, and the system stays down anyway, held there by its own response.

:::

::: think Exponential backoff and jitter are the standard advice. Would they have stopped it?

Not here. Backoff waits longer between attempts, and jitter randomises the waits, so the retries arrive at different times. Neither changes how many retries there are. Four calls per transfer, against a service with a third more capacity than normal demand, is overload whatever the timing. In the lab, backoff, and backoff with jitter, both stayed down for the whole run, just like immediate retries.

Jitter solves a different problem: many clients that failed at the same moment retrying at the same moment. Backoff helps calls that fail at the end of a short blip. What stops a storm is limiting the *number* of retries.

:::

::: think Retries made things worse. Should you remove them?

No. Most failures are isolated: one request lost on the network, one instance restarting. Retrying those is cheap and makes them invisible. In the lab, with one fraud decision in twenty failing at random, no retries meant 85 of 1,600 transfers failed; with retries, none did, at the cost of 81 extra calls. The goal is retries that help with isolated failures, and stop when failures are widespread.

:::

## The mental model

### Retries multiply load exactly when there's least capacity

A retry is more work, sent to a service that just failed. When failures are rare, that costs almost nothing. When they're widespread, every failure becomes up to N more calls, so the load multiplies at the moment the service is least able to take it.

Layers multiply too. If the app retries three times, and the API retries three times, and the API's client library retries three times, one tap can become 4 × 4 × 4 = 64 calls.

### What to retry

- **Transient failures**: timeouts, connection errors, 503 and 429, 500 with care. Not 400, 401, 403, 404 or 422: they'll fail the same way again.
- **Idempotent operations**: a fraud decision, a read, a request with an idempotency key (B3 stage 3). Not a payment without one.
- **When the server says so**: a `Retry-After` header on a 429 or 503 tells you when.

### Limit the volume, not just the timing

<<< @/../samples/src/Bank.Api/Endpoints/FraudGate.cs#retry

A *retry budget* caps retries at a fraction of recent calls, here 10%, plus a few to spare. When a dependency fails occasionally, the budget is never reached. When it fails widely, retries stop growing with the failures: at most 1.1 times the normal load, instead of four times.

<<< @/../samples/src/Bank.Api/Endpoints/FraudGate.cs#retry-budget

### Try it

The simulator models the lab: 150 transfers a second, a fraud service with a fixed capacity and a queue, and a slow spell. Compare the retry policies, then give the service more capacity, or let it drop requests whose callers have gone, which stage 1's deadlines make possible.

<RetryStormSimulator />

## The escalation ladder

### 1. Retry transient failures, a few times

Retry timeouts, connection errors, 503s and 429s, at most two or three times, only for idempotent operations.

**Fixes:** isolated failures. In the lab, 85 failed transfers out of 1,600 became none.
**Costs:** a few extra calls (81 in that run) and a little latency for the retried requests.
**Breaks:** under widespread failure, as above: the load multiplies.

### 2. Back off exponentially

Wait longer before each retry: 200 ms, then 400, then 800.

**Fixes:** calls that fail near the end of a short blip, which succeed on a later attempt. In the lab, with the fraud service down for three seconds and plenty of spare capacity, failed transfers fell from 239 with immediate retries to 126.
**Costs:** latency for retried requests.
**Breaks:** nothing, but it doesn't reduce the number of retries, so it doesn't prevent a storm.

### 3. Add jitter

Randomise each wait, so clients that failed together don't retry together.

**Fixes:** synchronised retries, such as thousands of clients reconnecting at the same moment after a restart.
**Costs:** nothing.
**Breaks:** nothing, but like backoff, it doesn't reduce the volume. In the lab's sustained overload it made no difference.

### 4. A retry budget

**Fixes:** retry storms. In the lab's slow spell, the service recovered about 16 seconds after it began, against never with unlimited retries, with 321 retries in total.
**Costs:** during a widespread failure, requests that a retry might have saved fail instead: 2,503 failures, against 1,497 with no retries at all.
**Breaks:** nothing. It's the setting that makes retries safe to have.

### 5. Retry at one layer

Choose one place to retry, usually the one closest to the failing dependency, and make the layers above fail fast instead.

**Fixes:** multiplication across layers.
**Costs:** agreement between teams, and client libraries whose retries can be switched off.
**Breaks:** nothing.

### 6. Let the server shed abandoned work

If the dependency can drop a request whose caller has already given up, a storm burns out on its own. Stage 1's deadlines tell it when to; the simulator shows the difference.

**Fixes:** the backlog of abandoned work that sustains a storm.
**Costs:** changes on the server side.
**Breaks:** nothing.

## Trade-offs at a glance

| Retry policy | Isolated failures | Short outage, spare capacity | Slow spell near capacity |
|---|---|---|---|
| None | 85 of 1,600 failed | 240 failed | Recovered in ~10 s; 1,497 failed |
| 3 immediate retries | — | 239 failed; 4× calls during the outage | Never recovered; 5,250 failed |
| 3 retries, exponential backoff | — | 126 failed | Never recovered |
| 3 retries, backoff and jitter | 0 failed (with a budget) | 173 failed | Never recovered |
| 3 immediate retries, 10% budget | — | 240 failed | Recovered in ~16 s; 2,503 failed |

## Traps

- **Retrying everything.** 4xx responses fail the same way again.
- **Retrying non-idempotent operations.** A retried payment without an idempotency key is a second payment.
- **Retries at every layer.** They multiply.
- **Trusting backoff and jitter to prevent storms.** They spread retries; they don't reduce them.
- **Ignoring `Retry-After`.** The server told you when to come back.

::::: trap
One retry pipeline, shared by every outbound call in a service. It was copied from an older document and translated to the current Polly API. What's wrong with it?

```csharp
public sealed class Outbound(HttpClient http)
{
    // One retry pipeline, used for every outbound call.
    private static readonly ResiliencePipeline Retry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TimeoutException>(),
            MaxRetryAttempts = 5,
            Delay = TimeSpan.Zero,
        })
        .Build();

    public async Task PayAsync(OutgoingPaymentRequest payment, CancellationToken ct) =>
        await Retry.ExecuteAsync(async token =>
        {
            using var response = await http.PostAsJsonAsync("/payments", payment, token);
            response.EnsureSuccessStatusCode();
        }, ct);
}
```

::: details Answer
- **It retries a payment with no idempotency key.** If the first attempt reached the server and the reply was lost, the retry pays again.
- **It retries permanent failures.** `EnsureSuccessStatusCode` throws `HttpRequestException` for a 400 or a 422 too, so a rejected payment is sent five more times.
- **It doesn't retry the timeouts it means to.** `HttpClient` reports a timeout as `TaskCanceledException`, not `TimeoutException`, so `Handle<TimeoutException>()` never matches.
- **Six immediate attempts, with no budget.** During a widespread failure, it sends six calls for every one, at once.

Use the HTTP retry options from `Microsoft.Extensions.Http.Resilience`, which handle transient HTTP failures correctly, with two or three attempts, backoff and jitter, a budget, and an idempotency key on every payment.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "How do you design retries?" Or: "What's a retry storm, and how do you prevent one?"

**Short answer:** "Retry only transient failures, like timeouts, connection errors, 503s and 429s, only for idempotent operations, a small number of times, with exponential backoff and jitter, and honouring `Retry-After`. But backoff and jitter only spread retries out; they don't reduce them. When a dependency fails widely, every failure turns into more calls, and if it's near capacity, the retries can keep it overloaded after the original problem is gone: a metastable failure. So I'd cap retries with a budget, a fraction of normal traffic, retry at only one layer, and pair it with a circuit breaker that stops calling a dependency that's clearly down."

::: details Follow-up: why does jitter matter, then?
When many clients fail at the same moment, such as when a server restart drops all its connections, they'd all retry on the same schedule, in waves. Jitter randomises each wait, so the waves smooth out. It's cheap and worth having, but it addresses timing, not volume.
:::

::: details Follow-up: how do retries multiply across layers?
Each layer's attempts are multiplied by the layers below it. Three retries at three layers means up to (1 + 3)³ = 64 calls for one user action. Retry at one layer, usually the one nearest the dependency, and have the others fail fast or pass the error up.
:::

::: details Follow-up: what's a metastable failure?
A state where a system stays broken after the trigger is gone, because its own reaction keeps it there. A retry storm is the classic case: a short slowdown causes timeouts, retries add load, the extra load causes more timeouts. Recovery takes removing the extra load: shedding it, stopping retries, or opening a circuit breaker.
:::

::: details Follow-up: when should you not retry at all?
For non-idempotent operations without an idempotency key, for client errors (4xx except 429), when the deadline is nearly used up, and when the dependency is known to be down, which is what a circuit breaker tells you. And at layers other than the one you've chosen to retry at.
:::
:::::

## Try it

From the `samples` folder, with the lab and the fake services running. Give the fraud service a fixed capacity, 10 decisions at a time taking 50 ms each, so 200 a second, with room to queue:

```sh
curl -X POST localhost:5090/fraud/settings -H "Content-Type: application/json" \
  -d '{"delayMs": 50, "slowMs": 1000, "capacity": 10, "queueLimit": 2000}'
```

Start the API with the fraud call before the transaction and a 500 ms timeout:

```sh
dotnet run --project src/Bank.Api -- --Lab:Transfers=Event --Lab:FraudGate=BeforeTransaction --Fraud:TimeoutMs=500
```

Send 150 transfers a second for 50 seconds, with the fraud service slow for three seconds from 15 seconds in, and read the timeline:

```sh
dotnet run --project tools/Bank.Load -- --outage --seconds 50 --rate 150 --blip-at 15 --blip-seconds 3 --blip-kind slow
```

Then restart the API with each retry policy and run it again:

- `--Fraud:Retries=3 --Fraud:RetryDelayMs=0`
- `--Fraud:Retries=3 --Fraud:Backoff=Exponential --Fraud:RetryDelayMs=200`
- the same, with `--Fraud:Jitter=true`
- `--Fraud:Retries=3 --Fraud:RetryDelayMs=0 --Fraud:RetryBudgetPercent=10`

For a short outage with spare capacity, use `--rate 80` and leave out `--blip-kind slow` (the default is down). For isolated failures, set `"failureRate": 0.05` and run without a blip.

Our results are in the table above. The slow-spell runs, second by second around the blip:

| Policy | Transfers succeeding from 15 s | Calls to the fraud service | Recovered |
|---|---|---|---|
| None | 0 a second, until 25 s | 150 a second | At 25 s |
| 3 immediate retries | 0 a second, to the end | About 600 a second, 400 of them refused | Not within 50 s |
| 10% retry budget | 0 a second, until about 31 s | About 150 a second | At about 31 s |

## What breaks next

With a budget, retries can't start a storm. But when the fraud service is properly down, for ten minutes, every transfer still tries the call, waits up to half a second for a timeout, then fails. Customers wait for nothing, and the fraud service, trying to restart, is still hit by 150 calls a second.

Next: [3. Stop calling what's down](./03-stop-calling-whats-down).
