---
title: "Sync or async between services"
track: backend
journey: one-service-becomes-many
stage: 4
level: intermediate
sources: [03-architecture/microservices.md, 11-domain-driven-design/domain-events.md]
status: written
---

# 4. Sync or async between services

## Where we are

Statements no longer need the ledger to be up. Transfers do need other services, and more of them each quarter. Paying a payee now means the ledger asking the payees service who the payee is, then a customers service whether the account is in good standing, a limits service whether the amount is allowed, and the fraud service whether it looks right. Each is its own deployment, each is available 99.9% of the time, and each is called in turn while the customer waits.

Transfers are less available than any one of them, and slower than all of them added together. In the lab, with each check failing 1% of calls (exaggerated, so a one-minute run shows it), 96.5% of transfers went through, with a median of 95 ms. When the fraud service was down for ten seconds, every transfer sent in those ten seconds failed.

## Stop and think

::: think Four services, each available 99.9% of the time. How available are transfers?

If their failures are independent, 0.999 × 0.999 × 0.999 × 0.999 ≈ 99.6%. That's about three hours a month when a transfer can fail, against 43 minutes for any one service. Latency adds up the same way: in sequence, the transfer takes the sum of the four calls, and it's slow whenever any one of them is slow, so a 1-in-100 slow call becomes a 1-in-25 slow transfer.

:::

::: think The fraud service is down for ten minutes. What should a customer who pays someone see?

That's a business decision, not a technical one. The options are to refuse with a clear "try again shortly" (what happens now), to make the payment without the check for small amounts (B4 stage 5), or to accept it now and make it when the fraud service is back, telling the customer it's on its way. Most customers paying a bill would rather hear "accepted, processing" than "failed"; someone at a till waiting for confirmation may not.

:::

::: think Which of the four calls really have to happen while the customer waits?

Fewer than it seems. The checks must happen before money moves, but not necessarily while the customer is waiting. If the transfer is accepted first and made afterwards, the only thing the customer's request needs is the ledger's own database. And anything that happens *after* the money moves, such as notifications, statements or fraud scoring for learning, never belongs in the chain at all: that's what events are for (B2).

:::

## The mental model

### Three ways services talk

| Style | The caller says | The caller waits? | Who must be up? |
|---|---|---|---|
| **Request and response** | "Tell me" or "do this, and tell me how it went" | Yes | Both, at the same moment |
| **Command, sent asynchronously** | "Do this", to one service, through a queue | No; it learns the outcome later | Only the queue, when sending |
| **Event** | "This happened", to whoever is interested | No; it doesn't know who listens | Only the queue, when publishing |

The cost of request and response is *temporal coupling*: both sides must be working at the same moment. Every synchronous call in a request adds one more service that must be up for the request to succeed, and its latency to the request's.

### What a chain costs

For a request that needs every service in a chain:

- **Availability multiplies.** n services at availability *a* give *aⁿ*, if their failures are independent.
- **Latency adds up**, in sequence. In parallel, the request waits for the slowest call instead.
- **The tail gets fatter.** If 1 call in 100 is slow, a request making n calls meets a slow one with probability 1 − 0.99ⁿ: about 1 in 25 with four calls, whether they're in sequence or in parallel.

<ChainCalculator />

### Make the chain cheaper, or shorter, or optional

In the lab, the ledger calls the payees service over HTTP, then three checks in the fake services, each taking about 25 ms, slow in 1 call in 100, and failing in 1 in 100. The checks can be called in sequence or in parallel, with or without one retry:

<<< @/../samples/src/Bank.Modular.Ledger/Checks.cs#run-checks

Each line is 1,200 transfers, at 20 a second for a minute; the parallel line is two runs:

| How the ledger calls | Transfers made | Median | 99th percentile |
|---|---|---|---|
| In sequence | 96.5% | 95 ms | 321 ms |
| In parallel | 96.8–97.6% | 41–45 ms | 269–271 ms |
| In parallel, retrying a failure once | 100% | 45 ms | 267 ms |
| ... with the fraud service down for 10 seconds | 83.2%: all 200 sent while it was down failed | 43 ms | 266 ms |

Parallel calls halved the median but not the tail, and didn't change availability. A retry recovered every independent failure. Neither helped when a service was actually down.

So the ledger can also *accept* a transfer without asking anyone, and make it afterwards. Accepting needs only the ledger's own database:

<<< @/../samples/src/Bank.Modular.Ledger/LedgerModule.cs#accept

A processor makes accepted transfers in the background. When a dependency can't answer, it tries again later, with backoff, instead of failing the transfer:

<<< @/../samples/src/Bank.Modular.Ledger/TransferRequests.cs#process-requests

With the same ten-second fraud outage:

| | Answered | Transfers made | From request to made |
|---|---|---|---|
| Accept now, make afterwards | 1,200 of 1,200, with 202, median 5 ms | 100%, including all 200 sent while fraud was down | median about 0.5 s; 99th percentile 11.7 s, the ones that waited out the outage |

The load tool checks each accepted transfer's status every half second, so those times are accurate to about half a second.

What accepting costs: the customer gets "accepted" rather than "done", so they need a way to follow it (`GET /transfers/{id}`) and to hear the outcome; and a transfer can be accepted and then rejected later, for example for insufficient funds, unless the money is held when the transfer is accepted (B3 stage 6).

## The escalation ladder

### 1. Make each call reliable

Timeouts on every call, retries for idempotent calls with backoff and a budget, and a circuit breaker for each dependency (all of B4).

**Fixes:** independent, transient failures. In the lab, one retry took transfers from about 97% to 100%.
**Costs:** a little latency on retries, and configuration to get right.
**Breaks:** nothing, but it can't help when a dependency is down for minutes. In the lab, every transfer failed for the ten seconds fraud was down.
**Don't use it when:** the call isn't idempotent. Retrying a payment without an idempotency key pays twice.

### 2. Call in parallel

Make independent calls at the same time.

**Fixes:** latency adding up. The lab's median went from 95 ms to 41–45 ms.
**Costs:** a little more code; and more load at once on the dependencies.
**Breaks:** nothing, but it doesn't help availability or the tail, and only works for calls that don't need each other's answers.

### 3. Take services out of the chain

Keep a copy of data that can be slightly stale (stage 3), such as an account's standing, updated by the customers service's events; or move a check into the service that needs it, if the boundary was wrong (stage 2).

**Fixes:** a call removed is a failure and a delay removed.
**Costs:** staleness, and the machinery of keeping a copy.
**Breaks:** decisions that need current data. A copy that says an account is in good standing a second after it was frozen will let one more payment through; the business must decide whether that's acceptable.

### 4. Accept now, finish later

Record the request, answer at once, and do the work in the background, retrying until the dependencies answer.

**Fixes:** temporal coupling. In the lab, every transfer was accepted during a fraud outage, and all of them were made once it ended.
**Costs:** a pending state, a way for customers to follow and be told the outcome, a processor to run and monitor, and rejections that arrive after acceptance.
**Breaks:** the immediate answer. If the customer must know *now*, as at a till, this rung isn't available.
**Don't use it when:** the caller needs the result to continue.

## Trade-offs at a glance

| Option | Fixes | Costs | Breaks | Reach for it when |
|---|---|---|---|---|
| Reliable calls | Transient, independent failures | Configuration, retry latency | Nothing; no help in outages | Always |
| Parallel calls | Latency adding up | Concurrent load | Nothing; no help with availability | Independent calls |
| Fewer services in the chain | Each removed call's failures and latency | Copies, staleness | Decisions that need current data | Data that can be slightly old |
| Accept now, finish later | Temporal coupling | Pending states, status, notifications | The immediate answer | The caller can wait for the outcome |

## Traps

- **Long synchronous chains**, A calls B calls C calls D, often grown one reasonable call at a time. Draw them; the drawing is usually a surprise.
- **Retries at every layer.** Three layers each retrying three times turn one failure into 27 calls (B4 stage 2).
- **Treating parallel calls as an availability fix.** They help latency only.
- **Events used as hidden request and response:** publish an event, then wait for a reply event. That's temporal coupling with extra steps.
- **Accepting without holding the money.** A transfer accepted and then rejected for insufficient funds is a broken promise to the customer.

::::: trap
A client for the payments service, from an older design document. It's registered with `AddHttpClient`, and it "has resilience built in". What's wrong with it?

```csharp
public class PaymentServiceClient : IPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    public PaymentServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://payment-service");
        _circuitBreaker = Policy
            .Handle<HttpRequestException>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }

    public Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request) =>
        _circuitBreaker.ExecuteAsync(async () =>
        {
            var response = await _httpClient.PostAsJsonAsync("/api/payments", request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PaymentResult>())!;
        });
}

// Registration
services.AddHttpClient<IPaymentService, PaymentServiceClient>(client =>
{
    client.BaseAddress = new Uri(configuration["Services:Payment"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(500)));
```

::: details Answer
- **It retries a payment.** `POST /api/payments` isn't idempotent, and there's no idempotency key, so a timeout after the payment was made, followed by a retry, pays twice (B3 stage 3). Retry only with a key the payments service deduplicates on.
- **The breaker never opens.** Typed clients are created per use, so each instance has its own breaker, and no breaker ever sees five failures (B4 stage 3). Breakers must be shared, for example in the resilience pipeline registered with the client.
- **The constructor overwrites the configured address** with a hard-coded one, so `Services:Payment` is silently ignored.
- **The timeout is 10 seconds, retried three times with waits:** a caller can wait over 40 seconds, longer than anyone upstream will (B4 stage 1).
- **And the call is synchronous at all.** If the caller doesn't need the payment's result to answer its own request, a command through a queue removes the payments service from its chain.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "Should services communicate synchronously or asynchronously?" Or: "What's temporal coupling?"

**Short answer:** "Synchronous calls couple services in time: both must be up at once, so a request that needs several services is less available than any of them, availabilities multiply, and its latency adds up. I use synchronous calls where the caller genuinely needs the answer to continue, and make each one reliable with timeouts, idempotent retries and breakers, and parallel where possible. Everything after the decision goes out as events. And where the caller can wait for the outcome, I accept the request, answer at once, and do the work in the background, retrying until dependencies are back, with a status the caller can follow."

::: details Follow-up: how does the client find out what happened to an accepted request?
Return 202 with a `Location` for the request's status, and a `Retry-After` hint for polling. Better, notify: a push notification or email to a customer, a webhook to a partner. Either way, the status must be durable, so it survives restarts, and final: accepted, then completed or rejected with a reason.
:::

::: details Follow-up: when is synchronous the right choice?
When the caller can't do anything useful without the answer: a query for data to show, a validation the user is waiting on, an authorisation at a till. Then keep the chain short, make the calls reliable, run independent ones in parallel, and decide in advance what happens when a dependency is down.
:::

::: details Follow-up: what's the difference between a command and an event?
A command asks one specific service to do something, and can be refused: "reserve R500". An event states that something happened, for anyone who cares: "transfer completed". The sender of a command depends on its receiver; the publisher of an event doesn't know who listens, which is what makes events the loosest coupling, and also the hardest to trace.
:::
:::::

## Try it

From the `samples` folder, with Postgres running. Run each process with a smaller connection pool, since three processes with 100 connections each would exceed Postgres's default limit of 100:

```sh
POOL="--ConnectionStrings:Modular=Host=localhost;Port=5433;Database=bank;Username=bank;Password=bank;Maximum Pool Size=40"
```

1. **Start the dependencies:** the fake services, with every check failing 1% of calls, and the payees module in a process of its own:

   ```sh
   dotnet run --project tools/Bank.FakeServices
   curl -X POST localhost:5090/checks/settings -H "Content-Type: application/json" \
     -d '{"delayMs": 20, "jitterMs": 10, "slowRate": 0.01, "slowMs": 250, "failureRate": 0.01}'
   dotnet run --project src/Bank.Modular -- --urls http://localhost:5101 --Modules:Run=Payees "$POOL"
   ```

2. **Start the ledger**, which calls payees and three checks for every transfer:

   ```sh
   dotnet run --project src/Bank.Modular -- --Modules:Run=Ledger --Modules:PayeesUrl=http://localhost:5101 \
     --Transfers:Checks=customers,limits,fraud --Lab:Checks=Sequential "$POOL"
   ```

3. **Send a minute of transfers**, then restart the ledger with `--Lab:Checks=Parallel`, then add `--Transfers:CheckRetries=1`:

   ```sh
   dotnet run --project tools/Bank.Load -- --chain --seconds 60 --rate 20 --payees-url http://localhost:5101
   ```

4. **Take the fraud service down for ten seconds**, part-way through, with retries on; then restart the ledger with `--Lab:Transfers=Accept` and do it again:

   ```sh
   dotnet run --project tools/Bank.Load -- --chain --seconds 60 --rate 20 --payees-url http://localhost:5101 \
     --blip fraud --blip-at 20 --blip-seconds 10
   ```

   `curl localhost:5100/transfers/<transferId>` shows an accepted transfer's status.

5. **Classify the calls.** For each call a transfer makes, decide: must it happen before the money moves, and must it happen while the customer waits? Which could be a copy, which a check after acceptance, and which an event afterwards?

## What breaks next

The bank launches prepaid cards, run by a cards service with its own database. Topping up a card moves money out of the customer's account, in the ledger, and onto the card, in the cards service: two services, two databases, no transaction that covers both. One evening, a top-up debited the customer's account, then the cards service failed. The card was never credited, and nothing undid the debit.

Next: [5. A transaction across services](./05-sagas).
