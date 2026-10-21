---
title: "One front door"
track: backend
journey: one-service-becomes-many
stage: 6
level: intermediate
sources: [03-architecture/api-gateway-patterns.md, 03-architecture/microservices.md]
status: written
---

# 6. One front door

## Where we are

The services have settled: ledger, payees, statements, cards, notifications. The mobile app talks to all of them directly, each at its own address. Its home screen needs the balance, recent activity, payees and card top-ups, so it makes one call after another, and on a mobile connection each one costs a round trip. Every service checks the customer's token itself, in its own way: one validates the token's audience and one doesn't, and one, it turns out, never checked that the account in the URL belonged to the customer asking.

In the lab, the home screen needs four calls to three services. With 150 ms added to each request, as a mobile network's round trip would, loading it took a median of 633 ms.

## Stop and think

::: think Every service checks tokens for itself. What goes wrong?

The same rule, written several times, drifts: different libraries, different settings, one service that forgot a check. Rotating the signing key means changing every service. Every service is exposed to the internet, so every one is attack surface. And the other things every request needs, such as rate limits, TLS, request ids and logging, are repeated too. A single front door can do the checks every request needs, once. It can't do all of them, though: whether payee X belongs to the customer is something only the payees service knows.

:::

::: think The app makes four calls in sequence, at 150 ms a round trip. What could make the screen faster?

Making the calls in parallel from the app: in the lab, 167 ms. Or one call to an endpoint built for that screen, which makes the four calls inside the data centre, where round trips cost a millisecond: also 167 ms. The single call's advantage in the lab wasn't speed. It returned a fifth of the data, because it sent only what the screen shows; it kept working, with a gap, when one service was down; and the screen's composition can change without releasing a new app.

:::

::: think The gateway sees every request. What should it never do?

Make business decisions. Fees, limits, eligibility and the rules of a transfer belong to the services that own them; put them in the gateway and every other way in, such as the partner API or a batch job, bypasses them, while every team now needs changes to one shared component. That's a new monolith, in the one place everything depends on. The gateway's job is the concerns every request shares: routing, authentication, coarse authorisation, rate limits, and passing on who the caller is.

:::

## The mental model

### What goes at the front door, and what stays behind it

| Concern | Gateway | Service |
|---|---|---|
| Routing a path to the service that owns it | Yes | — |
| TLS, request ids, access logs | Yes | Its own logs too |
| Is the token valid, and who is this? | Yes, once | Trusts the gateway's answer |
| Rate limits per client (B4 stage 6) | Yes | Its own limits for its own capacity |
| May this customer use *this account*? (in the path) | Yes, coarsely | Yes, again |
| May this customer use *this payee* or *this transfer*? | No: it doesn't know | Yes |
| Fees, limits, eligibility, workflows | Never | Yes |

The gateway checks what it can see in the request; the services check what only they know. Checking ownership again in the service is defence in depth, not duplication: a service reached some other way, by a partner API or a bug in a route, still refuses what it should (B9).

### The lab's gateway

`src/Bank.Gateway` is built on [YARP](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/yarp-overview), Microsoft's reverse proxy library. Routes and services are configuration:

```json
"Routes": {
  "payees":   { "ClusterId": "payees", "Order": 1, "AuthorizationPolicy": "customer", "RateLimiterPolicy": "per-customer",
                "Match": { "Path": "/accounts/{accountId}/payees/{**rest}" } },
  "accounts": { "ClusterId": "ledger", "Order": 2, "AuthorizationPolicy": "customer", "RateLimiterPolicy": "per-customer",
                "Match": { "Path": "/accounts/{accountId}/{**rest}" } }
},
"Clusters": {
  "ledger": { "Destinations": { "main": { "Address": "http://localhost:5100" } } },
  "payees": { "Destinations": { "main": { "Address": "http://localhost:5101" } } }
}
```

At the edge it checks the token, limits each customer's rate, and passes on the identity it verified, replacing anything the client sent in its place:

<<< @/../samples/src/Bank.Gateway/Program.cs#edge

And it refuses a customer another customer's account, which it can do because the account is in the path:

<<< @/../samples/src/Bank.Gateway/Program.cs#ownership

That last point shapes APIs: a resource addressed as `/accounts/7/card-top-ups` can be checked at the edge; one addressed as `/card-top-ups?accountId=7`, or only by an id, can't. The lab's ledger lists a customer's top-ups under their account for that reason, and keeps its list of every top-up off the gateway.

### A backend for each frontend

A *backend for frontend* (BFF) is an endpoint, or a small service, shaped for one client: here, the mobile app's home screen. It makes the calls the screen needs inside the data centre, in parallel, and returns only what the screen shows. If one service can't answer, it returns the rest, and says which part is missing:

<<< @/../samples/src/Bank.Gateway/Home.cs#home

Different clients get different BFFs: the mobile app, the web app and partners want different shapes, and a single BFF serving all of them becomes the shared component every team waits on. The team that builds the client usually owns its BFF.

### In the lab

Each line loads the home screen 200 times, four screens at once, with the ledger, payees and statements each in its own process:

| Home screen | Network round trip | Median | 99th percentile | Data a screen |
|---|---|---|---|---|
| Four calls, straight to the services | none | 15 ms | 358 ms | 3.7 KB |
| Four calls, through the gateway | none | 28 ms | 343 ms | 3.7 KB |
| Four calls in sequence, through the gateway | 150 ms | 633 ms | 643 ms | 3.7 KB |
| Four calls in parallel, through the gateway | 150 ms | 167 ms | 201 ms | 3.6 KB |
| One call to the BFF | 150 ms | 167 ms | 279 ms | 0.7 KB |
| One call to the BFF, statements stopped | 150 ms | 161 ms | 174 ms | 0.3 KB, without recent activity |

The gateway added about 13 ms to four calls, roughly 3 ms each for the extra hop and the token check. Requests without a token were refused with 401 before reaching any service; a customer asking for another's account got 403. The load tool adds the 150 ms itself, before each request; it doesn't model a mobile network's bandwidth or how many requests it will run at once, both of which favour fewer, smaller requests.

## The escalation ladder

### 1. Every service at its own address, checking everything

**Fixes:** nothing; it's where the trigger started.
**Costs:** every cross-cutting concern, written in every service.
**Breaks:** consistency of security checks, key rotation, and the client's patience.

### 2. A reverse proxy for routing

One address; paths routed to the services that own them.

**Fixes:** clients knowing every service's address; services exposed directly.
**Costs:** one more hop (milliseconds), and a component to run with no single point of failure: several instances, no state.
**Breaks:** nothing, if it stays a router.

### 3. An API gateway

The proxy also authenticates, rate-limits, checks ownership where the path allows, and forwards a verified identity.

**Fixes:** checks that drift between services; a request without a token never reaches one.
**Costs:** about 3 ms a call in the lab; and the discipline to keep business rules out.
**Breaks:** services that trust the gateway's identity header if they can also be reached *without* the gateway. Lock the network down, or verify the identity again (B9).
**Don't use it when:** there's one service; a framework's middleware does the same job.

### 4. A backend for each frontend

Endpoints shaped for one client's screens, composing calls inside the data centre.

**Fixes:** chatty clients, oversized responses, and screens that break when one service is down. In the lab, a fifth of the data, and a home screen that survived statements stopping.
**Costs:** another thing to build and run, per client type, with its own release cycle.
**Breaks:** nothing, if it only composes and shapes. A BFF that starts making decisions is the same trap as a smart gateway.
**Don't use it when:** the client is a single web page that can call in parallel and use what it gets; the latency gain may be nothing.

## Trade-offs at a glance

| Option | Fixes | Costs | Breaks | Reach for it when |
|---|---|---|---|---|
| Services at their own addresses | Nothing | Repeated concerns | Consistent security | Never, past one service |
| Reverse proxy | Many addresses | A hop | Little | Several services |
| API gateway | Drifting checks, open services | A hop, discipline | Services reachable around it | Several services, public clients |
| BFF per client | Chatty clients, payload size, partial failure | One per client type | Nothing, if it stays thin | Mobile apps, very different clients |

Requests *between* services, rather than from outside, are a different problem: identity between services, encryption, retries and timeouts on internal calls. That's the territory of service meshes, covered with the rest of where code runs in B7.

## Traps

- **Business rules in the gateway.** Every other way in skips them, and every team queues to change the gateway.
- **Services that trust an identity header from anyone.** If a service can be reached without the gateway, anyone can claim to be anyone.
- **One gateway instance, or a stateful one.** It's in front of everything; run several, and keep state out of it.
- **Retrying non-idempotent requests at the gateway.** A retried `POST /transfers` is a second transfer (B3 stage 3).
- **Gateway timeouts longer than the client's,** so it keeps working on requests nobody is waiting for (B4 stage 1).
- **One BFF for every client,** which becomes the next shared bottleneck.

::::: trap
A change to the gateway, so the ledger team "doesn't have to deal with it". It passes review. What's wrong?

```csharp
app.MapReverseProxy(proxy =>
{
    proxy.Use(async (context, next) =>
    {
        // Internal tools send X-Customer-Account themselves; customers get theirs from the token.
        if (!context.Request.Headers.ContainsKey("X-Customer-Account"))
            context.Request.Headers["X-Customer-Account"] = context.User.FindFirstValue("account_id");

        // Transfers over R5,000 need a second factor; all transfers pay a 0.5% fee.
        if (context.Request.Path.StartsWithSegments("/transfers") && HttpMethods.IsPost(context.Request.Method))
        {
            var transfer = await context.Request.ReadFromJsonAsync<TransferRequest>();
            if (transfer!.Amount > 5000 && !context.Request.Headers.ContainsKey("X-Second-Factor"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Request.Headers["X-Fee"] = (transfer.Amount * 0.005m).ToString(CultureInfo.InvariantCulture);
        }
        await next();
    });
});
```

::: details Answer
- **Anyone can be anyone.** A client that sends its own `X-Customer-Account` keeps it, so the services act for whichever account it names. The gateway must always remove the incoming header and set it from the verified token, as the lab's does; internal tools need their own authenticated route.
- **The second factor is a header the client sets.** Anything a client sends can be forged; the second factor must be verified, by whatever issued it.
- **Reading the body consumes it.** Unless the request is buffered first, the ledger receives an empty body.
- **The rules are in the wrong place.** The limit and the fee belong to the ledger. Transfers that arrive another way, such as the partner API, a batch file or an internal tool, skip them, and the ledger trusts an `X-Fee` header that, on those routes, the caller can set to zero.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "What's an API gateway, and what would you put in it?" Or: "What's a backend for frontend?"

**Short answer:** "A single entry point that routes requests to the services that own them and handles what every request needs: TLS, authentication, rate limits, request ids, and coarse authorisation such as whether the account in the path is the caller's. It passes the verified identity on, and services still check ownership of what only they know. It never makes business decisions, or it becomes a new monolith. A backend for frontend is an endpoint shaped for one client: it composes the calls a screen needs inside the data centre and returns only what the screen shows, which suits mobile apps, with one per client type."

::: details Follow-up: authentication at the gateway or in each service?
Both, at different depths. The gateway validates the token once, so invalid requests go no further, and forwards a verified identity. Services authorise against their own data, and either trust that identity, if the network guarantees only the gateway can reach them, or verify a token themselves. Never let a service trust an identity header it could receive from anywhere.
:::

::: details Follow-up: isn't the gateway a single point of failure?
It's a single point of *entry*, which needn't be a single point of failure: run several instances behind a load balancer, keep them stateless, and give them their own capacity planning and limits. Keep them simple, too: the less logic in the gateway, the less there is to fail and the fewer deploys it needs.
:::

::: details Follow-up: API gateway or service mesh?
Different traffic. A gateway handles requests coming in from outside (north–south): clients, authentication, rate limits. A mesh handles traffic between services (east–west): mutual TLS, retries, timeouts and telemetry, usually through a proxy beside each service. Many systems have both.
:::
:::::

## Try it

From the `samples` folder, with Postgres running, each in its own terminal. Give each process a smaller connection pool so they fit within Postgres's connection limit:

```sh
POOL="--ConnectionStrings:Modular=Host=localhost;Port=5433;Database=bank;Username=bank;Password=bank;Maximum Pool Size=30"
dotnet run --project src/Bank.Modular -- --Modules:Run=Ledger --Modules:PayeesUrl=http://localhost:5101 "$POOL"
dotnet run --project src/Bank.Modular -- --urls http://localhost:5101 --Modules:Run=Payees "$POOL"
dotnet run --project src/Bank.Modular -- --urls http://localhost:5102 --Modules:Run=Statements \
  --Modules:LedgerUrl=http://localhost:5100 --Lab:Statements=Copy "$POOL"
dotnet run --project src/Bank.Gateway
```

1. **Knock on the door.** Get a lab token for account 7, then try without it, with it, and for someone else's account:

   ```sh
   T=$(curl -s -X POST "localhost:5110/lab/token?accountId=7" | tr -d '"')
   curl -i localhost:5110/accounts/7
   curl -i -H "Authorization: Bearer $T" localhost:5110/accounts/7
   curl -i -H "Authorization: Bearer $T" localhost:5110/accounts/8
   ```

2. **Load the home screen** each way, with a mobile round trip:

   ```sh
   dotnet run --project tools/Bank.Load -- --home calls --rtt 150
   dotnet run --project tools/Bank.Load -- --home calls --rtt 150 --parallel
   dotnet run --project tools/Bank.Load -- --home bff --rtt 150
   ```

   Then measure the gateway's own cost: `--home calls --rtt 0`, with and without `--direct`.

3. **Stop the statements process**, and call `GET localhost:5110/app/home` with the token. The balance and payees are still there; `unavailable` says what isn't.

4. **Sort the concerns.** For each, decide gateway, BFF or service: a maximum transfer amount; blocking an IP address that's guessing passwords; the order of payees on the home screen; whether a payee belongs to the customer; adding a request id; a partner's monthly quota.

## What breaks next

With a front door in place, the payees team wants to move payees out of the monolith properly: a service of their own, rewritten on a new data model with its own database. Their plan is the obvious one. Build it, copy the data across on a Saturday night, point the gateway at the new service, and switch off the old module on Sunday. If anything is wrong on Monday, there's no way back: the old module's data is already a day out of date.

Next: [7. Moving a capability without stopping](./07-strangler-fig).
