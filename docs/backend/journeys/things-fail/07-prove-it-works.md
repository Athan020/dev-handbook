---
title: Prove it works before you need it
track: backend
journey: things-fail
stage: 7
level: advanced
sources: [02-system-design/redundancy-fault-tolerance.md]
status: written
---

# 7. Prove it works before you need it

## Where we are

The API has timeouts, budgeted retries, a circuit breaker, bulkheads, fallbacks and rate limits, and it's been months since the last incident. Then the fraud service has a 30-second outage, and the breaker doesn't open. Every transfer waits half a second and fails, and the fraud service is hit by every call while it tries to recover.

The breaker was enabled. Its minimum number of calls had been copied from a much busier service: 1,000 calls in a 10-second window. At 80 transfers a second, the window can only ever hold 800. The breaker could never have opened.

In the lab, with that one setting, the 30-second outage played out exactly as if there were no breaker: 505 ms for every refused transfer, and 4,800 calls to the fraud service.

## Stop and think

::: think The breaker was in the code and enabled in configuration. How could nobody notice it couldn't open?

Because it does nothing on a normal day. Resilience code only acts during failures, so on every ordinary day it gives no signal at all, working or broken. And whether it can work depends on its settings *combined with* real traffic: 1,000 was a sensible minimum for the service it came from. Nothing exercised the combination until the outage did.

:::

::: think How could you have caught it before the outage?

Several ways, from cheapest:

- Do the arithmetic: 80 calls a second for 10 seconds is 800, below 1,000.
- A test that drives the configured breaker with realistic traffic and failures, on a simulated clock, and checks it opens. In the lab's tests, that takes milliseconds.
- An alert on the breaker's state, and a dashboard showing it has never once opened, despite the fraud service's errors.
- Practising the outage on purpose: making the fraud service fail in a test environment, or carefully in production, and watching what happens.

:::

::: think Isn't deliberately breaking things in production reckless?

Not if it's controlled, and it's often less reckless than the alternative. The failure will happen anyway, at a time you didn't choose, with nobody ready. A planned experiment has a hypothesis ("the breaker opens within ten seconds and small transfers continue"), a small blast radius (one instance, a small share of traffic), a way to stop at once, and people watching. Start in test environments, and move to production only once each step is boring.

:::

## The mental model

### Resilience code is emergency-only code

Code that runs only during failures is, by default, untested in the conditions it exists for. Its settings interact with traffic, timing and each other in ways no code review sees. So it needs testing designed for it, at several levels:

| Level | What it exercises | How fast |
|---|---|---|
| Settings tests on a simulated clock | Whether the configured thresholds can trigger at real traffic | Milliseconds |
| Integration tests with fault-injecting fakes | The whole request path when a dependency hangs, fails or refuses | Seconds |
| Fault injection in a running environment | The deployed system, its configuration and its dashboards | Minutes |
| Game days | People too: alerts, runbooks, decisions, communication | Hours |

### Testing time without waiting for it

A breaker's behaviour depends on time: sampling windows, break durations. Tests that wait in real time are slow, so they get skipped or deleted. Instead, give the pipeline a clock you control. Polly's pipelines take a `TimeProvider`, and Microsoft's `FakeTimeProvider` lets a test move time forward instantly:

<<< @/../samples/tests/Bank.Api.Tests/ResilienceSettingsTests.cs#breaker-opens

With the lab's settings, the breaker opens within a quarter of a simulated second of a fraud outage starting. With the copied minimum of 1,000, the same test shows it never opens, and fails the build instead of the next outage.

### Watch it working

Every resilience mechanism should leave a trace: breaker state changes, retries, fallbacks taken, requests refused by rate limits. Polly reports these as [metrics and logs](https://www.pollydocs.org/advanced/telemetry.html); the lab exposes its own counts at `GET /resilience/fraud`. Alert when a breaker opens, and look for mechanisms that never fire when the dependency has clearly had trouble.

## The escalation ladder

### 1. Check the settings against real traffic

For each mechanism, work out whether it can trigger at the traffic it will actually see.

**Fixes:** the arithmetic mistakes, like the copied minimum.
**Costs:** minutes per dependency.
**Breaks:** nothing, but it's easy to skip, and it goes stale when traffic changes.

### 2. Test the settings on a simulated clock

Build the configured pipeline in a unit test, drive it with simulated traffic and failures, and assert that it opens, recovers and retries as intended.

**Fixes:** settings that can't work, caught in the build. The lab's tests run in milliseconds.
**Costs:** building the pipeline so a test can supply the clock.
**Breaks:** nothing.

### 3. Test the request path with injected faults

Replace the dependency with a fake that hangs, fails or refuses on cue, and assert what the API does: the status code, how fast it answers, what it records. The lab's integration tests do this for the timeout, the breaker and every fallback.

**Fixes:** wiring mistakes: a timeout whose token never arrives, a breaker created per call, a fallback that's never reached.
**Costs:** fakes that can misbehave, and tests that check timings as well as answers.
**Breaks:** nothing.

### 4. Inject faults into a running system

Make a real dependency slow or unavailable in a running environment: the lab's fake services do it on request; elsewhere, a proxy such as [Toxiproxy](https://github.com/Shopify/toxiproxy) between services, or a service mesh's fault injection. Watch the dashboards, not just the API.

**Fixes:** problems only the deployed system has: configuration, networks, dashboards that don't show the breaker. In the lab, it shows the misconfigured breaker behaving exactly like no breaker at all.
**Costs:** an environment, and care to limit the damage.
**Breaks:** whatever the experiment touches, deliberately. Keep the blast radius small and have an abort switch.

### 5. Game days

A planned exercise: inject a realistic failure and have the team respond as if it were real, with a hypothesis, a stop condition and a write-up afterwards.

**Fixes:** the human side: alerts nobody reads, runbooks that are wrong, decisions nobody owns, such as who decides to switch the fraud fallback on.
**Costs:** people's time, and planning.
**Breaks:** nothing, if planned well.

## Trade-offs at a glance

| Approach | Catches | Costs | When |
|---|---|---|---|
| Settings arithmetic | Thresholds that can't trigger | Minutes | Every change |
| Simulated-clock tests | The same, automatically | Some test code | Every build |
| Fault-injection integration tests | Wiring mistakes | Fakes, timing assertions | Every build |
| Faults in a running environment | Configuration, networks, dashboards | An environment, care | Regularly |
| Game days | People and process | Time and planning | A few times a year |

## Traps

- **Copying resilience settings between services.** They depend on traffic.
- **Tests that inject nothing.** If no fault is injected, a passing test proves nothing.
- **Real waits in tests.** Slow tests get skipped; use a controllable clock.
- **Never looking at resilience metrics.** A breaker that has never opened may be one that can't.
- **Chaos without a stop button.** Every experiment needs an abort and a small blast radius.

::::: trap
A test, added after the incident, named "transfers survive a fraud outage". It passes in CI. What does it prove?

```csharp
[Fact]
public async Task Transfers_survive_a_fraud_outage()
{
    await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
        .UseSetting("Lab:FraudGate", "BeforeTransaction")
        .UseSetting("Fraud:Breaker", "true"));
    var client = api.CreateClient();

    var response = await client.PostAsJsonAsync(
        "/transfers", new TransferRequest(30, 31, 10m, "Outage test"), TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
}
```

::: details Answer
Almost nothing.

- **No fault is injected.** It passes in CI only because nothing is listening at the fraud service's address there, so the call is refused. On a machine where the fake services are running, the same test fails. It's testing the environment, not the code.
- **One request can't open a breaker.** The breaker needs a minimum number of calls in its window, so this test would pass identically with the breaker disabled, or misconfigured with a minimum of 1,000, the bug it was written for.
- **It checks the status code, not the behaviour.** The point of the breaker is failing *fast* and sparing the fraud service; the test checks neither the time taken nor the calls made.

Inject the fault explicitly with a fake handler, send enough calls to reach the breaker's minimum, and assert that later calls fail quickly without reaching the fake. Then test the configured thresholds against realistic traffic on a simulated clock, which is what would have caught the copied setting.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "How do you know your resilience patterns actually work?" Or: "What is chaos engineering?"

**Short answer:** "Resilience code only runs during failures, so by default it's never tested in the conditions it exists for, and its settings depend on real traffic. I'd test at several levels: unit tests that drive the configured timeouts, retries and breakers with simulated traffic on a fake clock, so they run in milliseconds; integration tests with fakes that hang or fail, asserting the API answers quickly and correctly; fault injection in a running environment, watching the dashboards too; and occasional game days that exercise the people and runbooks. And I'd monitor the mechanisms themselves: breaker state changes, retries, fallbacks, so a breaker that never opens is noticed."

::: details Follow-up: what's chaos engineering?
Running controlled experiments that inject failures into a system to find weaknesses before real failures do. Each has a hypothesis about steady-state behaviour, a limited blast radius, an abort condition, and observation. It starts in test environments and moves to production gradually, once the experiments are routine.
:::

::: details Follow-up: how do you test time-dependent code without slow tests?
Inject the clock. .NET's `TimeProvider` abstraction is accepted by Polly and much of the framework, and `FakeTimeProvider` lets a test advance time instantly. Ten simulated seconds of a failing dependency take milliseconds, so the tests can run on every build.
:::

::: details Follow-up: what would you monitor?
For each dependency: latency and error rate as you see them, breaker state and transitions, retries and the retry budget's use, fallbacks taken, bulkhead rejections; and for callers, rate-limit refusals per client. Alert on a breaker opening and on fallbacks in use. Review mechanisms that never fire.
:::
:::::

## Try it

From the `samples` folder.

1. **Run the settings tests.** They build the fraud gate's pipeline on a fake clock and check whether the breaker can open at 80 calls a second:

   ```sh
   dotnet test --project tests/Bank.Api.Tests --filter-class "Bank.Api.Tests.ResilienceSettingsTests"
   ```

   Change the lab's minimum in the first test to 1,000 and watch it fail.

2. **Run the fault-injection tests**, which put a fake fraud service in front of the API that hangs, fails or refuses:

   ```sh
   dotnet test --project tests/Bank.Api.Tests --filter-class "Bank.Api.Tests.FraudGateTests"
   ```

3. **Reproduce the incident in the lab.** With the fake services running and the fraud service set to hang during a slow spell (`'{"delayMs": 20, "slowMs": 90000}'`), start the API with the copied setting, and run stage 3's outage:

   ```sh
   dotnet run --project src/Bank.Api -- --Lab:Transfers=Event --Lab:FraudGate=BeforeTransaction \
     --Fraud:TimeoutMs=500 --Fraud:Breaker=true --Fraud:BreakerMinimumThroughput=1000
   dotnet run --project tools/Bank.Load -- --outage --seconds 60 --rate 80 --blip-at 15 --blip-seconds 30 --blip-kind slow
   ```

   `GET localhost:5080/resilience/fraud` reports the breaker still closed, with no openings. Our run: 2,400 transfers refused after 505 ms each and 4,800 calls to the fraud service, identical to stage 3's run with no breaker at all.

4. **Plan a game day.** Write the hypothesis for a fraud-service outage: what should happen, within how long, what customers see, and who decides to switch the fallback on. Then list how you'd stop the experiment if it went wrong.

## What breaks next

This is the end of the journey. You started with a transfer API that hung whenever the fraud service did, and made it survive slow, failing and absent dependencies, and callers who send too much, and then made sure those protections would actually work when needed.

Now put it all together: [the capstone](./capstone).
