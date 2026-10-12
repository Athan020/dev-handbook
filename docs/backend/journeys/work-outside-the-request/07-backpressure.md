---
title: The queue keeps growing
track: backend
journey: work-outside-the-request
stage: 7
level: advanced
sources: [06-cloud/messaging-events.md, 06-cloud/autoscaling-deep-dive.md]
status: written
---

# 7. The queue keeps growing

## Where we are

Failures are handled now. Then the last working day of the month arrives. Payroll runs, and thousands of salaries arrive in a minute, each with an email to send. In the lab, 6,000 transfers arrived in under a minute. The email provider takes 300 ms per email and accepts 20 calls at once. With one worker sending 5 emails at a time, the email queue peaked at about 5,000 messages, and the last "salary received" email went out 5 minutes after the money arrived.

## Stop and think

::: think The email queue holds 5,000 messages. Is that a problem?

It depends entirely on how fast it drains. 5,000 messages at 1,000 a second is five seconds of work; at 16 a second it's five minutes. Depth alone tells you little.

Watch how old the oldest waiting message is, and whether the queue is growing or shrinking: arrival rate against processing rate. Alert on how late the work is, not on how much of it there is.

:::

::: think The queue is growing, so you add more workers. When does that stop helping?

When something downstream becomes the limit. Here, the email provider accepts 20 calls at once. Up to that, more concurrency helps: in the lab, one worker sending 20 at a time instead of 5 cut the worst delay from 5 minutes to 45 seconds.

Beyond it, more workers only collect refusals. Three workers trying 60 calls at once got 9,990 "429 Too Many Requests" responses; those failures used up their retries, and 685 of the 6,000 emails ended up in the dead-letter queue, never sent.

:::

::: think Should the transfer API stop accepting transfers while the email queue is long?

No. The queue exists so that transfers don't wait for emails. A long queue means the emails are late, not that transfers should fail.

Push back where someone can slow down without harm instead: pace the payroll batch job, defer or drop work that's lost its value, and send urgent messages ahead of bulk ones. Backpressure belongs where waiting is acceptable.

:::

## The mental model

### Rates, not sizes

```mermaid
flowchart LR
    P["Producers: payroll, customers"] -- "arrival rate" --> Q["queue"]
    Q -- "processing rate" --> W["workers × concurrency"]
    W --> E["email provider: 20 calls at once"]
```

- The queue grows at *arrival rate − processing rate*, and shrinks at the reverse.
- Processing rate = *min(workers × concurrency, downstream limit) ÷ time per message*. With a 300 ms email and a provider limit of 20 calls at once, that's at most about 66 emails a second, however many workers you run.
- How late a message is ≈ *messages ahead of it ÷ processing rate*. That's Little's Law again, from B1 stage 4.

A queue absorbs a burst when the processing rate exceeds the average arrival rate. It turns the burst into delay instead of failure. If processing is slower than the *average* arrival rate, no queue is long enough.

### Backpressure, shedding and prioritising

Three ways to respond when work arrives faster than it can be done:

- **Backpressure**: tell producers to slow down. A batch job paces itself; a bounded queue refuses new messages, and the producer holds them.
- **Load shedding**: drop work that's no longer worth doing, such as a "balance changed" notification superseded by a newer one.
- **Prioritising**: do important work first. Separate queues for urgent and bulk messages, each with its own workers.

## The escalation ladder

### 1. Measure lag, not depth

Track the age of the oldest message in each queue, the arrival and processing rates, and the time to drain at the current rates. RabbitMQ's management API and Prometheus metrics expose queue depth and rates; the age of the oldest message is often easiest to measure in the consumer, from a timestamp in the message.

**Fixes:** noticing, before customers do.
**Costs:** a dashboard and alerts.
**Breaks:** nothing.

### 2. More concurrency per worker, up to the downstream limit

Email sending is mostly waiting on the network, so one worker can send many at once (`Worker:Concurrency`), with a prefetch at least that large.

**Fixes:** throughput, cheaply. In the lab, one worker sending 20 at a time instead of 5: the queue peaked at 2,889 instead of 5,044, drained 48 seconds after the batch instead of 310, and the worst delay fell from 310 seconds to 45.
**Costs:** more memory and connections per worker.
**Breaks:** the downstream service, once concurrency exceeds what it accepts.

### 3. More workers, with autoscaling on lag

Run more instances, and scale them on queue lag rather than CPU: these workers spend most of their time waiting, so CPU stays low while the queue grows. Tools such as KEDA scale on queue metrics.

**Fixes:** throughput, when one worker's resources are the limit.
**Costs:** more instances to run.
**Breaks:** the downstream limit, again. In the lab, three workers sending 20 at a time against a provider that takes 20 got 9,990 refusals, and 685 emails ended up dead-lettered after exhausting their retries. Set the autoscaler's maximum from the downstream limit.

### 4. Respect the downstream limit explicitly

Cap total concurrency or rate at what the provider accepts, divided across workers. Treat a 429 as "slow down": honour its `Retry-After` header, and don't let rate limiting use up the attempts meant for real failures.

**Fixes:** refusals and the retry storms they cause.
**Costs:** coordinating a limit across instances, which usually means a per-instance share, or a shared rate limiter.
**Breaks:** nothing. The queue holds the excess, which is its job.

### 5. Prioritise, and drop work that's lost its value

Send transaction confirmations through their own queue and workers, separate from bulk and marketing email. Give messages that go stale a time to live, so they're discarded instead of delivered late.

**Fixes:** urgent work stuck behind bulk work, and effort spent on pointless messages.
**Costs:** more queues to manage, and decisions about what may be dropped.
**Breaks:** fairness, deliberately, and dropped messages are gone.

### 6. Push back on producers that can wait

Pace batch jobs to the processing rate. Or bound the queue, so it refuses new messages when full and the publisher has to hold them. RabbitMQ can do this with a [maximum length and a `reject-publish` overflow policy](https://www.rabbitmq.com/docs/maxlength). With an outbox (stage 4), refused events simply wait in the outbox, and the relay tries again.

**Fixes:** unbounded growth, and a broker running out of memory or disk. When RabbitMQ hits its memory or disk alarm, it blocks every publisher, not just the busy one.
**Costs:** the producer must be able to wait: a batch job can, a customer's transfer shouldn't.
**Breaks:** anything that publishes without being ready for a refusal.

## Trade-offs at a glance

| Option | Fixes | Costs | Watch out for |
|---|---|---|---|
| Measure lag | Noticing in time | Dashboards, alerts | Alerting on depth alone |
| More concurrency per worker | Throughput, cheaply | Memory, connections | The downstream limit |
| More workers, scaled on lag | Throughput | Instances | Scaling past the downstream limit |
| Explicit downstream limit | Refusals, retry storms | Coordination | 429s using up retry attempts |
| Prioritise and expire | Urgent work first | More queues; drop decisions | Dropping what mattered |
| Backpressure on producers | Unbounded growth | Producers that can wait | Publishers unprepared for refusals |

## Traps

- **Scaling consumers past what the downstream service accepts.** Refusals rise, retries are spent, and work is dead-lettered.
- **Autoscaling on CPU.** I/O-bound workers look idle while the queue grows.
- **A prefetch far larger than a worker can handle.** One worker hoards messages that idle workers could be processing.
- **Treating the broker as infinitely large.** A memory or disk alarm stops every publisher.
- **One queue for urgent and bulk messages.** A marketing campaign delays every payment confirmation.

::::: trap
To speed up a batch run, a developer publishes messages in batches. This came from an older design document. What's wrong with it?

```csharp
public async Task EnqueueBatchAsync(IEnumerable<TransactionRequest> transactions)
{
    using var batch = await _sender.CreateMessageBatchAsync();

    foreach (var tx in transactions)
    {
        var message = new ServiceBusMessage(JsonSerializer.Serialize(tx));
        if (!batch.TryAddMessage(message))
        {
            // Batch full, send and create new
            await _sender.SendMessagesAsync(batch);
            batch = await _sender.CreateMessageBatchAsync();
            batch.TryAddMessage(message);
        }
    }

    await _sender.SendMessagesAsync(batch);
}
```

::: details Answer
It doesn't compile: `batch` is declared with `using var`, and a using variable can't be reassigned (error CS1656). The "obvious" fix, dropping `using`, leaks every batch.

Once it compiles, there's a quieter bug. If one message is too large to fit even in an empty batch, the second `TryAddMessage` returns `false`, the result is ignored, and that message is silently dropped.

Dispose each batch after sending it, and check the second `TryAddMessage`: if it fails, the message can't be sent in a batch at all, so throw or send it separately. And remember what stage 4 says about publishing outside a transaction.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "A queue keeps growing. What do you do?" Or: "How many consumers do you need?"

**Short answer:** "First I'd look at rates, not depth: arrival against processing, and how old the oldest message is. If processing is slower than arrivals on average, I'd add capacity: more concurrency per worker for I/O-bound work, then more workers, scaled on lag rather than CPU. But only up to the downstream limit: past it, you just collect refusals and burn retries. For bursts, the queue is doing its job, turning load into delay, so I'd make sure the delay is acceptable, prioritise urgent work on its own queue, expire work that goes stale, and push back on producers that can wait, like batch jobs."

::: details Follow-up: how do you size the number of consumers?
Processing rate per consumer = concurrency ÷ time per message. You need enough consumers for the expected arrival rate plus headroom to drain bursts in an acceptable time: (burst size ÷ acceptable drain time) + normal rate. Cap the total at the downstream service's limit.
:::

::: details Follow-up: what's backpressure, and where does it belong?
Signalling upstream to slow down, so work waits at its source rather than piling up downstream. It belongs where producers can afford to wait, such as batch jobs and internal pipelines, through pacing, bounded queues or flow control. It doesn't belong between a customer and their transfer: there, use the queue as the buffer.
:::

::: details Follow-up: what do you monitor on a queue?
Age of the oldest message, arrival and processing rates, consumer count and utilisation, the dead-letter queue's depth, and the redelivery rate. Alert on age and on the trend, not on raw depth.
:::

::: details Follow-up: the downstream API rate-limits you. How do you handle it?
Treat 429s as a signal to slow down, not as failures: honour `Retry-After`, and don't spend retry attempts on them. Cap concurrency or rate at the provider's limit across all workers. Let the queue hold the excess, and prioritise what matters if the backlog will take a while.
:::
:::::

## Try it

### The simulator

Two hours of email work, with a payroll batch at minute 30. Try the scenarios, then change workers, concurrency and the provider's limit yourself. Hover over the chart to see how late an email is at any moment.

- **Payroll, one worker**: the queue grows for as long as the batch lasts, then drains slowly.
- **Payroll, at the provider limit**: a lower peak, and a much faster drain.
- **More workers than the provider allows**: no better than the previous scenario. The extra attempts are refused.
- **Bounded queue**: the queue never exceeds its limit, and the producer has to hold what's refused.

<BacklogSimulator />

### Against the lab

With the API in `--Lab:Transfers=Event` mode, from the `samples` folder. Make the email provider realistic: 300 ms per email, and at most 20 calls at once.

```sh
curl -X POST localhost:5090/settings -H "Content-Type: application/json" \
  -d '{"emailDelayMs": 300, "fraudDelayMs": 50, "failureRate": 0, "maxConcurrentEmails": 20}'
```

1. **Run one email worker, five at a time, and send a payroll burst** of 6,000 salaries. Watch the `email.transfer-completed` queue in the management UI as it grows and drains:

   ```sh
   dotnet run --project src/Bank.Worker -- --Worker:Role=email --Worker:Concurrency=5 --Worker:Prefetch=10 --Worker:Retries=Backoff
   dotnet run --project tools/Bank.Load -- --payroll 6000 --concurrency 32
   ```

   When it has drained, `curl localhost:5090/stats` shows how late the emails were (`emailLagMs`).

2. **Match the provider's limit.** Reset the stats (`curl -X POST localhost:5090/reset`), and repeat with `--Worker:Concurrency=20 --Worker:Prefetch=40`.

3. **Exceed it.** Run three workers with `--Worker:Concurrency=20` each. Compare `rateLimited` in the stats, and look in `email.transfer-completed.dead`.

Our results, for a burst of 6,000 transfers in under a minute:

| Email workers | Peak queue | Drained after the burst | Email delay, p50 | Email delay, worst | Refused calls | Never sent |
|---|---|---|---|---|---|---|
| 1 × 5 at a time | 5,044 | 310 s | 151 s | 310 s | 0 | 0 |
| 1 × 20 (the provider's limit) | 2,889 | 48 s | 23 s | 45 s | 0 | 0 |
| 3 × 20 | 22 in the main queue | still going after 10 min | 1.6 s, for those sent | 37 s, for those sent | 9,990 | 685, dead-lettered |

The third row's queue looks healthy because the work had moved into the retry queues, failing there. Depth alone would have hidden the problem.

## What breaks next

This is the end of the journey. You started with a transfer endpoint that made customers wait for emails, and built up, one problem at a time, to reliable, ordered, idempotent, retrying, scalable asynchronous work. Each step solved one problem and exposed the next.

Now put it all together: [the capstone](./capstone).
