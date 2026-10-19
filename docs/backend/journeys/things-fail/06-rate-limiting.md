---
title: Protect yourself from your callers
track: backend
journey: things-fail
stage: 6
level: advanced
sources: [09-system-design-prep/02-rate-limiter.md, 01-fundamentals/algorithms-data-structures.md]
status: written
---

# 6. Protect yourself from your callers

## Where we are

The API now survives its dependencies failing. Then the danger comes from the other side. A payroll provider, a partner that submits salaries through the API, deploys a bug, and its integration starts retrying every request in a tight loop: a thousand requests a second, from one client. Nothing is down. Every customer's transfers just take ten seconds.

In the lab, with customers sending 80 transfers a second and the partner 1,000, customers' transfers still succeeded, but the median took 10.2 seconds instead of 16 milliseconds.

## Stop and think

::: think One partner is sending a thousand requests a second. Why are other customers affected?

Because they all share one API's capacity. The API can do a few hundred transfers a second; the partner alone asks for more than that, so requests queue, and every customer's request waits behind the partner's. Nothing protects customers from one caller's appetite. Stage 4's bulkheads did that for dependencies; this needs the same idea for callers.

:::

::: think You add a limit of 200 requests a second for the whole API. What happens to customers now?

They're refused. A single, shared limit doesn't distinguish callers: the partner uses most of the allowance, and customers find it already spent. In the lab, with a shared limit of 200 a second, only 22% of customers' transfers got through, while the partner got 188 a second. The limit protected the API and punished the wrong people.

The limit has to be per caller: each client gets its own allowance, so one client's flood exhausts only its own.

:::

::: think You run two instances of the API, each limiting every client to 50 requests a second. How many does the partner get?

Up to 100: 50 from each instance, because each counts on its own. In the lab, the partner got 102 a second through two instances, twice the limit. With N instances, an in-memory limit is N times what you wrote down, and it changes whenever you scale. For an exact limit, the instances must share a count: in the lab, a token bucket in Redis held the partner to 53 a second across both.

:::

## The mental model

### Who to limit, and by what

A rate limit needs a *key*: whose requests are counted together. Usually that's the authenticated client or API key; sometimes a user, an account, or an IP address as a last resort. Behind a load balancer or a mobile carrier's network, many callers share an IP, so limiting by IP can block a whole office or a whole city. Different keys can have different limits: a payroll partner may be allowed more than an anonymous caller.

### Four algorithms

- **Fixed window**: count requests in each whole second (or minute) and reset at the boundary. Simple and cheap, but a client can send its full allowance just before a boundary and again just after, twice the limit in a moment.
- **Sliding window**: count requests in the last second, whenever "now" is. Smooth and exact, at the cost of remembering recent requests; implementations often approximate it by splitting the window into segments.
- **Token bucket**: a bucket holds up to B tokens, refilled at R a second; each request takes one. Allows bursts up to B, then a steady R. The most common choice for APIs.
- **Concurrency limit**: at most N requests *in progress* at once, rather than per second. Protects against slow requests as well as many requests (B1 stage 4).

<RateLimiterVisualiser />

### When the limit is reached

Answer `429 Too Many Requests` with a `Retry-After` header, quickly, before doing any real work. A well-behaved client waits that long. Make refusals cheap: in the lab, a refused request took about a millisecond, so even a thousand a second of them barely loaded the API.

### Limits across instances

Each instance's in-memory limiter knows only its own traffic. For a limit that holds however many instances you run, they must share the count, usually in a fast shared store such as Redis, updated atomically. That adds a network round trip to every request, and a dependency: if the store is unavailable, decide whether the limiter fails open (allow) or closed (refuse). The lab fails open, because an unavailable rate limiter shouldn't take the API down with it.

ASP.NET Core's built-in limiter, partitioned by client:

<<< @/../samples/src/Bank.Api/Endpoints/RateLimits.cs#in-memory

A token bucket shared by every instance, as one atomic Redis script:

<<< @/../samples/src/Bank.Api/Endpoints/RateLimits.cs#token-bucket-script

## The escalation ladder

### 1. Cap your own concurrency

Admit only as many requests at once as the API can serve well, and refuse the rest quickly (B1 stage 4).

**Fixes:** overload making every request slow.
**Costs:** refused requests.
**Breaks:** fairness: it refuses whoever arrives when full, which during a flood is mostly everyone else.

### 2. A global rate limit

One limit for all callers together.

**Fixes:** total load.
**Costs:** almost nothing.
**Breaks:** the innocent: in the lab, customers got 22% of their transfers through, while the flooding partner got 188 a second.

### 3. A limit per client

Each client its own allowance, keyed by who they are.

**Fixes:** one caller's flood reaching everyone else. In the lab, with 50 a second per client, customers got 100% of their transfers through with a median of 13 ms, and the partner was held to 53 a second.
**Costs:** identifying clients reliably, and choosing limits per kind of client.
**Breaks:** nothing on one instance. On several, the limit multiplies.

### 4. A shared limit across instances

Keep the counts in a shared store.

**Fixes:** limits that multiply with instances. In the lab: 102 a second for the partner with in-memory limits on two instances; 53 a second with the bucket in Redis.
**Costs:** a round trip per request, and a store to run and protect.
**Breaks:** the API, if the store fails and the limiter fails closed. Fail open, and alert.

### 5. Limits that match the work

Different limits for different endpoints and costs: a cheap read and an expensive export shouldn't share an allowance. Weight requests by cost, give partners contracts with their limits written in, and tell clients their remaining allowance in response headers.

**Fixes:** expensive requests hiding inside a generous count.
**Costs:** more configuration, and agreements with partners.
**Breaks:** nothing.

## Trade-offs at a glance

| Setup (customers 80/s, partner 1,000/s, 30 s) | Customers succeeded | Customers' median | Partner got through |
|---|---|---|---|
| No partner | 100% | 16 ms | — |
| No limit | 100% | 10.2 s | 234 a second |
| Global limit, 200/s | 22% | — | 188 a second |
| 50/s per client, one instance | 100% | 13 ms | 53 a second |
| 50/s per client, in memory, two instances | 100% | 22 ms | 102 a second |
| 50/s per client, in Redis, two instances | 100% | 16 ms | 53 a second |

The partner gets 53 a second rather than 50 because the token bucket starts full: a burst of 100, then 50 a second.

## Traps

- **Limiting by IP address.** Many clients share one, behind proxies, offices and mobile networks.
- **One global limit.** It protects the API and punishes everyone but the culprit.
- **In-memory limits on several instances.** The real limit is N times what you set.
- **Refusing slowly.** A refusal that does real work, such as a database lookup, is still load.
- **No `Retry-After`.** Clients guess, usually by retrying at once.
- **A rate limiter that fails closed** when its store is down.

::::: trap
A home-made rate limiter, added to the API as middleware. It passes its unit tests. What goes wrong in production?

```csharp
public sealed class SimpleRateLimiter
{
    private readonly Dictionary<string, (long Second, int Count)> _counts = new();

    // At most 50 requests a second from each client.
    public bool Allow(HttpContext context)
    {
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var (current, count) = _counts.GetValueOrDefault(client);
        count = current == second ? count + 1 : 1;
        _counts[client] = (second, count);
        return count <= 50;
    }
}
```

::: details Answer
- **It isn't thread-safe.** Requests are handled concurrently, and a `Dictionary` being written from several threads can lose updates, or corrupt itself and throw. Counts come out wrong, and occasionally requests fail.
- **It keys by IP address.** Behind a load balancer, every request arrives from the load balancer's address, so all clients share one allowance of 50 a second. Without one, an office or a mobile network's users share an address.
- **It's a fixed window,** so a client can send 50 at the end of one second and 50 at the start of the next.
- **It's per instance,** so with several instances the limit multiplies.
- **It never forgets a client,** so memory grows with every address that ever called.

Use the framework's limiter, partitioned by an authenticated client id, with a token bucket or sliding window, and a shared store if you run more than one instance.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "How would you protect your API from a client sending too many requests?" Or: "Token bucket or sliding window?"

**Short answer:** "A rate limit per client, keyed by an authenticated client id rather than IP address, so one client's flood only exhausts its own allowance. A global limit protects the service but punishes everyone. I'd usually use a token bucket, which allows short bursts and a steady rate, refuse with 429 and a Retry-After header before doing any real work, and set different limits for different clients and endpoints. With more than one instance, I'd keep the counts in a shared store like Redis, updated atomically, failing open if it's unavailable, because in-memory limits multiply with the number of instances."

::: details Follow-up: compare the algorithms.
A fixed window is cheapest but lets a client send twice its limit around a boundary. A sliding window is exact but needs a log of recent requests, or an approximation with segments. A token bucket allows bursts up to its size, then a steady rate, and needs only two numbers per client. A leaky bucket smooths output to a constant rate, queuing the rest. A concurrency limit caps work in progress rather than rate, which also protects against slow requests.
:::

::: details Follow-up: how do you rate-limit across many servers?
Keep each client's state in a shared store, and update it atomically, for example with a Lua script in Redis, so two servers can't both take the last token. Use the store's clock rather than each server's. The cost is a round trip per request; to reduce it, servers can take tokens in small batches locally, accepting a little inaccuracy.
:::

::: details Follow-up: the rate limiter's store goes down. What happens?
Decide in advance. Usually fail open, allowing requests and alerting, because the limiter exists to protect the service, and refusing everyone is the outage it was meant to prevent. Where the limit is a hard contract, such as cost control on an expensive downstream call, failing closed may be right.
:::
:::::

## Try it

**In the browser.** The visualiser above sends one pattern of requests through three limiters. Try "Bursts at a window edge", and compare the busiest second each one let through; then "One big burst", with different bucket sizes.

**Against the lab.** From the `samples` folder, with the lab running. Customers send 80 transfers a second, each with their own client id, while one partner sends 1,000 a second:

```sh
dotnet run --project src/Bank.Api -- --Lab:Transfers=Event
dotnet run --project tools/Bank.Load -- --noisy --seconds 30
```

1. **A global limit:** restart the API with `--Lab:RateLimit=Global --Lab:RateLimitPerSecond=200 --Lab:RateLimitBurst=200`.
2. **A limit per client:** `--Lab:RateLimit=PerClient --Lab:RateLimitPerSecond=50 --Lab:RateLimitBurst=100`. Try `--Lab:RateLimitAlgorithm=FixedWindow` and `SlidingWindow` too.
3. **Two instances.** Start a second API on another port, and spread the load across both. Give each a smaller connection pool, because two instances with 100 connections each exceed Postgres's default limit of 100 (our first attempt failed with "too many clients", a scaling surprise of its own):

   ```sh
   POOL="--ConnectionStrings:Bank=Host=localhost;Port=5433;Database=bank;Username=bank;Password=bank;Maximum Pool Size=40"
   dotnet run --project src/Bank.Api -- --Lab:Transfers=Event "$POOL" --Lab:RateLimit=PerClient
   dotnet run --project src/Bank.Api -- --urls http://localhost:5081 --Lab:Transfers=Event "$POOL" --Lab:RateLimit=PerClient
   dotnet run --project tools/Bank.Load -- --noisy --seconds 30 --targets http://localhost:5080,http://localhost:5081
   ```

4. **A shared limit:** start Redis (`docker compose --profile redis up -d`), and add `--Lab:RateLimitStore=Redis` to both instances.

Our results are in the table above.

## What breaks next

The API is protected from its dependencies and from its callers: timeouts, budgeted retries, breakers, bulkheads, fallbacks and rate limits. Months pass without an incident. Then the fraud service has an outage, and the breaker doesn't open. It turns out it never could have: one of its settings made it impossible, and nobody had ever seen it open.

Next: [7. Prove it works before you need it](./07-prove-it-works).
