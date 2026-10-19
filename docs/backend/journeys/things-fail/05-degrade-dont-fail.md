---
title: Degrade, don't fail
track: backend
journey: things-fail
stage: 5
level: advanced
sources: [02-system-design/redundancy-fault-tolerance.md]
status: written
---

# 5. Degrade, don't fail

## Where we are

Timeouts, budgeted retries, a breaker and bulkheads mean a fraud outage can't hang the API or spread to other dependencies. But while the breaker is open, every transfer is refused, quickly and politely. For the half hour the fraud service is down, nobody can pay rent, buy groceries or split a bill. The fraud team's outage has become the bank's outage.

In the lab, during a 30-second fraud outage, 2,579 of 4,800 transfers were refused.

## Stop and think

::: think The fraud check is unavailable. Should a transfer go ahead without it?

That's not an engineering decision. Refusing every transfer (*failing closed*) means no fraud risk and a complete outage for customers. Allowing every transfer (*failing open*) means no outage and unlimited risk: in the lab, 2,560 transfers worth R6.3 million went through unchecked in 30 seconds. Somewhere between is a choice about how much risk the bank will accept to keep customers paying, and that choice belongs to the people who own the risk. Engineering's job is to offer the options, build the one chosen, and make it visible when it's in use.

:::

::: think You may let some transfers through unchecked. Which ones, and what happens to the rest?

The ones where the risk is small and bounded: small amounts, payees the customer has paid before, transfers between their own accounts. Every one is recorded, so it can be reviewed when the fraud service is back. The rest needn't be refused: they can be *held*, accepted now and made once a decision is possible, with the customer told it's being checked. In the lab, allowing transfers up to R1,000 let 518 through unchecked, worth R262,000, and held 2,062. All of them were made within about a minute and a half of the fraud service recovering.

:::

::: think The fraud service is back, with 2,062 held transfers waiting. What could go wrong releasing them?

Three things. Releasing them is load on a service that has only just recovered: in the lab, the release job added about 30 calls a second to the normal 80, the stage 2 problem in a new place, so pace it. Balances have changed while they waited, so some may now be refused for insufficient funds, and customers need to be told. And some will be declined, which is exactly what the check was for, so declines need a path back to the customer too.

:::

## The mental model

### A fallback is a decision, per dependency and per operation

For each dependency, ask what the business wants to happen without it, operation by operation. The usual options:

- **Fail fast, clearly**: refuse, with a message and a `Retry-After`. Right when acting without the dependency is unacceptable.
- **Reduced service**: do the low-risk part, such as small transfers, or a balance shown from a cache with its age.
- **Accept now, finish later**: hold the work, tell the customer, complete it when you can.
- **An alternative source**: a second provider, a cached or last-known answer, simpler local rules.
- **Fail open**: carry on as if the dependency had said yes. Only where being wrong is cheap.
- **Switch things off**: turn off non-essential features to protect the essential ones.

<<< @/../samples/src/Bank.Api/Endpoints/FraudGate.cs#fallback

Holding a transfer stores the request, and tells the customer it's being checked:

<<< @/../samples/src/Bank.Api/Endpoints/TransferEndpoints.cs#hold

A background job asks again, and makes or declines each held transfer:

<<< @/../samples/src/Bank.Api/Endpoints/HeldTransferReleaser.cs#release

### Degraded is a state, and it must be visible

Every decision made without the dependency is recorded, for review afterwards. Operations should see the API is degraded, from the breaker's state (stage 3) and from fallback counts. And customers should see it too, as "being checked", not as a silent difference in behaviour.

### Fallbacks rot

A fallback runs only during an outage, so it's the least tested code in the system: it may not work when it's finally needed. Stage 7 is about exercising it on purpose.

## The escalation ladder

### 1. Fail fast, clearly

Refuse with 503 and `Retry-After`, and a message the app can show.

**Fixes:** customers waiting for nothing (stage 3 already does this).
**Costs:** a full outage for that operation. In the lab, 2,579 of 4,800 transfers refused.
**Breaks:** nothing technical. The business may not accept it for long.

### 2. Reduced service: let low-risk work through

Allow transfers under a limit the business sets, and record each one for review.

**Fixes:** most of the outage, for most customers.
**Costs:** bounded risk: in the lab, 518 transfers worth R262,000 made without a fraud decision, all recorded.
**Breaks:** nothing, if the limit is right. Watch for abuse: someone who knows the fallback exists could split a large transfer into small ones, so cap the total per customer too.

### 3. Accept now, finish later

Hold what can't go ahead, answer 202, and complete it when the dependency is back.

**Fixes:** refusals for the rest. In the lab, 2,062 held transfers, all made within about a minute and a half of recovery.
**Costs:** storage, a release job, customer messages, and handling transfers that are declined or can no longer be afforded when they're released.
**Breaks:** the recovering dependency, if the release isn't paced: the lab's release job added about 30 calls a second.

### 4. Use another source

A second fraud provider, a cached "this payee is trusted", or simpler rules that run locally.

**Fixes:** the outage, with some decisions still made.
**Costs:** another integration to keep working, and decisions that may be less good than the main source's.
**Breaks:** nothing, but a second provider is one more dependency, with outages of its own.

### 5. Fail open

Carry on as if approved.

**Fixes:** the outage, completely.
**Costs:** all the risk the dependency existed to prevent: 2,560 transfers worth R6.3 million unchecked in the lab.
**Breaks:** trust, if it's chosen by default or silently. Reserve it for dependencies whose answer is a nice-to-have, such as recommendations or analytics, never for a control.

### 6. Switch off what isn't essential

Use feature switches to turn off expensive, non-essential features, such as statement exports or spending insights, during an incident, freeing capacity for payments. B6 covers feature flags.

**Fixes:** contention between essential and non-essential work.
**Costs:** building the switches before you need them.
**Breaks:** the switched-off features, deliberately.

## Trade-offs at a glance

| Fallback (30-second fraud outage, 4,800 transfers) | Made at once | Made without a check | Held | Refused |
|---|---|---|---|---|
| Fail closed | 2,221 | 0 | 0 | 2,579 |
| Fail open | 4,800 | 2,560 (R6.3 million) | 0 | 0 |
| Allow small (up to R1,000), hold the rest | 2,738 | 518 (R262,000) | 2,062, all made later | 0 |

## Traps

- **Failing open by accident**: a catch-all that returns "approved".
- **Fallbacks nobody decided.** The default in the code becomes the bank's risk policy.
- **A fallback that calls the failing dependency**, or a cache it can't reach either.
- **Unpaced recovery work** hitting a dependency that has just come back.
- **Silent degradation.** Nobody knows, so nobody reviews, and nobody fixes.

::::: trap
A fraud check written "to never block customers". What does it actually do?

```csharp
public sealed class FraudCheck(HttpClient http)
{
    private sealed record Decision(bool Approved);

    public async Task<bool> IsAllowedAsync(TransferRequest transfer, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/fraud/decision", transfer, ct);
            var decision = await response.Content.ReadFromJsonAsync<Decision>(ct);
            return decision!.Approved;
        }
        catch
        {
            return true; // don't block customers just because fraud checks are down
        }
    }
}
```

::: details Answer
It fails open for every transfer, of any size, silently.

- **Any failure approves**: a timeout, a 503 with no body, a malformed answer, even the caller cancelling. A fraudster who can make the fraud service slow has turned the check off.
- **Nothing is recorded**, so nothing is reviewed afterwards, and nobody knows how many transfers went through unchecked.
- **The status code is ignored.** A 500 whose body happens to parse is treated as a decision.

Make the fallback explicit and deliberate: decide which transfers may proceed without a decision (with a limit set by the business), record each one, hold or refuse the rest, and count fallbacks so the degraded state shows up on a dashboard.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "What happens to your service when a dependency is down?" Or: "Should this fail open or fail closed?"

**Short answer:** "It depends on the operation, and it's a business decision about risk, not a default in the code. For a fraud check on payments, failing open is unlimited risk, and failing closed is a full outage, so I'd propose something in between: let low-risk transfers through under a limit the business sets, record each for review, and hold the rest, accepting them and completing them when the fraud service is back, with the customer told it's being checked. Recovery work gets paced, so it doesn't overload the service that's just recovered. Degraded mode is visible on dashboards and to customers. And because fallbacks only run during outages, I'd test them deliberately."

::: details Follow-up: give examples of failing open and failing closed.
Fail closed: authentication, fraud and sanctions checks, payment authorisation: being wrong is expensive. Fail open: recommendations, analytics events, a personalised greeting: being wrong is cheap, and failing would block something important for something trivial.
:::

::: details Follow-up: what is graceful degradation?
Keeping the most important functions working, perhaps in a reduced form, when parts of the system fail, instead of failing completely: showing a cached balance with its age, accepting payments to process later, switching off non-essential features. It's designed in advance, per dependency, rather than improvised during the outage.
:::

::: details Follow-up: how do you stop people abusing the fallback?
Bound it: a limit per transfer, and per customer per hour or day while degraded, so a large transfer can't be split into many small ones. Record every unchecked transfer for review, and alert on unusual volumes of them.
:::
:::::

## Try it

From the `samples` folder, with the lab and the fake services running. As in stage 3, make the fraud service hang during a slow spell:

```sh
curl -X POST localhost:5090/fraud/settings -H "Content-Type: application/json" -d '{"delayMs": 20, "slowMs": 90000}'
```

1. **Fail closed.** Start the API with stage 3's breaker, and run a 30-second outage:

   ```sh
   dotnet run --project src/Bank.Api -- --Lab:Transfers=Event --Lab:FraudGate=BeforeTransaction \
     --Fraud:TimeoutMs=500 --Fraud:Breaker=true --Fraud:BreakerBreakSeconds=5 --Fraud:Fallback=FailClosed
   dotnet run --project tools/Bank.Load -- --outage --seconds 60 --rate 80 --blip-at 15 --blip-seconds 30 --blip-kind slow
   ```

2. **Fail open:** restart with `--Fraud:Fallback=FailOpen`.
3. **Allow small, hold the rest:** restart with `--Fraud:Fallback=AllowSmall --Fraud:FallbackLimit=1000`. Watch held transfers being made after the outage with `curl localhost:5080/fraud-reviews/summary`.

Our results are in the table above. With "allow small", 400 held transfers had been made when the run ended, 15 seconds after the fraud service recovered; the release job made the remaining 1,662 at about 22 a second, all within another 80 seconds.

## What breaks next

The API now survives its dependencies. Then a partner, a payroll provider that submits salaries through the API, deploys a bug: their integration retries every request in a tight loop. Thousands of requests a second arrive from one client, and every customer's transfers slow down.

Next: [6. Protect yourself from your callers](./06-rate-limiting).
