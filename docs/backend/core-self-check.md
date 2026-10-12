---
title: Backend core self-check
track: backend
status: written
---

# Backend core self-check

The backend journeys start from something you already own: a working REST API backed by a relational database. They assume a few fundamentals, and spend their words on judgement instead of re-teaching them.

Use this page to check yourself quickly. If an answer doesn't come easily, follow the links; they're better introductions than this handbook would be. You don't need to be an expert in any of this, just comfortable.

**Journey B1 (Your API gets popular) assumes:** HTTP and REST, SQL basics, and async/await in C#.

## HTTP and REST

You should be comfortable with:

- methods (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`), and which are safe or idempotent;
- status codes, especially 200, 201, 204, 400, 401, 403, 404, 409, 429 and 5xx;
- headers, including content negotiation and caching headers;
- designing resource URLs and paging collections.

::: think A client sends PUT /accounts/42/nickname twice, because the first response timed out. Is that a problem? What if it had been POST /accounts/42/transactions?

`PUT` is idempotent: sending the same request twice leaves the same result as sending it once, so a retry is safe. `POST` isn't. A retried `POST` that records a transaction can record it twice. Making retries of non-idempotent requests safe, usually with an idempotency key, comes up in journeys B2 and B3.

:::

::: think When should an API return 404, and when 400?

404 when the resource the URL names doesn't exist, such as an unknown account. 400 when the request itself is malformed or invalid, such as a page size of 1,000 when the maximum is 100. The lab API does both: `/accounts/999999/transactions` is a 404, and `?pageSize=101` is a 400.

:::

**Brush up:** [MDN's HTTP overview](https://developer.mozilla.org/en-US/docs/Web/HTTP/Overview), [HTTP methods](https://developer.mozilla.org/en-US/docs/Web/HTTP/Methods) and [status codes](https://developer.mozilla.org/en-US/docs/Web/HTTP/Status). For the definitive semantics, [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110).

## SQL basics

You should be comfortable with:

- `SELECT` with `WHERE`, `ORDER BY` and `LIMIT`;
- `JOIN` (inner and left), `GROUP BY` with aggregates, and `HAVING`;
- `INSERT`, `UPDATE` and `DELETE`;
- what a transaction is, and what `COMMIT` and `ROLLBACK` do;
- primary keys and foreign keys.

::: think Write a query for each account's number of transactions and total amount, for accounts with more than 1,000 transactions, busiest first.

```sql
SELECT account_id, count(*) AS transactions, sum(amount) AS total
FROM transactions
GROUP BY account_id
HAVING count(*) > 1000
ORDER BY transactions DESC;
```

`WHERE` filters rows before grouping; `HAVING` filters groups after it. You can run this in the playground below.

:::

::: think You want every transaction with its counterparty's name, including transactions that have no counterparty. Which join?

A `LEFT JOIN` from transactions to accounts on the counterparty id. An inner join would drop the transactions without a counterparty, because they have nothing to join to.

:::

Try your answers against the lab data:

```sql playground
SELECT account_id, count(*) AS transactions, sum(amount) AS total
FROM transactions
GROUP BY account_id
HAVING count(*) > 1000
ORDER BY transactions DESC;
```

**Brush up:** the [PostgreSQL tutorial](https://www.postgresql.org/docs/current/tutorial.html), especially "The SQL Language", covers all of this.

## async/await in C#

You should be comfortable with:

- `async` and `await`, and what `Task` and `Task<T>` represent;
- passing a `CancellationToken` through to I/O calls;
- why blocking on a task (`.Result`, `.Wait()`) is a problem;
- running independent operations concurrently with `Task.WhenAll`.

::: think What does a thread do while an awaited database call is in progress?

Nothing for this request. At the `await`, the method returns its thread to the thread pool, which uses it for other work. When the database replies, the method resumes, possibly on a different thread. That's how a server handles many concurrent requests with few threads, and why blocking a thread instead is costly. Stage 4 of B1 shows how costly.

:::

::: think Two independent queries each take 50 ms. How do you run them so the request takes about 50 ms, not 100? What's the catch with EF Core?

Start both, then await them together with `Task.WhenAll`. The catch: one EF Core `DbContext` can't run two queries at once. It throws if you try, so each concurrent query needs its own context, for example from `IDbContextFactory`.

:::

**Brush up:** Microsoft's [asynchronous programming with async and await](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/), and the [EF Core documentation](https://learn.microsoft.com/ef/core/) for the basics of querying and saving data.

## Ready?

If those felt comfortable, start [B1: Your API gets popular](/backend/journeys/your-api-gets-popular/). If one area didn't, spend an hour with its links first; the journey will make more sense for it.
