---
title: Backend track
track: backend
status: draft
---

# Backend track

Every backend journey starts from the same place: you own a working REST API for a banking product (accounts and transactions) backed by a relational database. It works in development. Then real life happens.

Each journey applies one kind of pressure and follows it outward, one stage at a time, until you can answer a realistic interview question with everything the journey built up.

## The backend T

The stem is what you're expected to own: HTTP and API design, SQL and data modelling, and your language and framework in depth. The [core self-check](./core-self-check) lists what the journeys assume, with links out for anything you need to brush up.

The bar is everything around that core which senior work and senior interviews push into: databases at scale, messaging, consistency, failure, architecture, operations and security. The journeys below cover the bar.

## Journeys

| Journey | Pressure | Capstone |
|---|---|---|
| [B1: Your API gets popular](./journeys/your-api-gets-popular/) | Traffic grows until the database can't keep up | Reads are degrading on a 10M-row table |
| [B2: Work that shouldn't happen in the request](./journeys/work-outside-the-request/) | Slow, unreliable work inside the request | Design a multi-channel notification system |

### Coming later

- **B3: The money must add up.** Transactions and isolation anomalies, optimistic vs pessimistic concurrency, idempotency keys, double-entry ledgers, reconciliation.
- **B4: Things fail.** Timeouts, retries with backoff and jitter, circuit breakers and bulkheads, graceful degradation, rate limiting.
- **B5: One service becomes many.** Modular monolith first, bounded contexts, data ownership, sync vs async between services, sagas, API gateways, strangler-fig migration.
- **B6: You shipped it, now what?** Logs, metrics and traces, SLOs, deployment strategies, feature flags, an incident walkthrough.
- **B7: Someone is trying to break it.** OAuth 2.0 and OIDC, JWT pitfalls, common web vulnerabilities, secrets handling, network boundaries.

## Suggested order

Start with the [core self-check](./core-self-check), then take B1 and B2 in order. Later journeys build on both.
