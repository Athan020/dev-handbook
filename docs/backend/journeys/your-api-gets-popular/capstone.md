---
title: "Capstone: slow reads on a 10M-row table"
track: backend
journey: your-api-gets-popular
stage: capstone
level: advanced
sources: [09-system-design-prep/08-troubleshoot-slow-reads.md, 09-system-design-prep/05-cheatsheet.md]
status: written
---

# Capstone: slow reads on a 10M-row table

> "You have a table with 10 million rows. Metrics show that read queries are getting slower. What do you do to improve read speed?"

Try answering out loud before reading on, in the five minutes or so you'd have in an interview. Then compare.

The trap in this question is the number. Ten million sounds big, and it tempts people straight to sharding, NoSQL or a cache. It isn't big. The whole journey's lab database is ten million rows, and it fits comfortably in the memory of a modest server. The question is really asking whether you diagnose before you fix.

## 1. Clarify

Good answers start with questions, because the right fix depends on the answers. Commit to your own first.

::: think What would you ask before proposing anything?

- **Which reads, and how slow?** One query or all of them? Which percentiles: is p50 slow, or only p99?
- **Since when, and what changed?** A deployment, a data migration, growth, a new feature, a new customer?
- **Slow when quiet, or only at peak?** That separates expensive work (stages 2 and 3) from queuing for resources (stage 4).
- **What's the read pattern?** By key, by range, aggregates? Hot keys, or spread evenly?
- **How fresh must reads be?** That decides whether caches and replicas are options at all.
- **What's the write rate?** It limits how many indexes you can afford, and how much replicas will lag.

:::

::: think The interviewer says: "The account transactions endpoint. p50 is fine; p99 has gone from 80 ms to 2 seconds over three months. Traffic has doubled." What does that tell you?

p50 being fine says the typical query is OK, so it isn't a broken database or a slow network for everyone. A rising p99 with growing traffic suggests something that's grown with the data or with load: busy accounts with far more rows than average, a query that sorts their whole history, offset paging that gets deeper, or queuing for connections at peak. Measure which, rather than guessing (stage 1).

:::

## 2. Estimate

Back-of-envelope numbers show which fixes are even plausible. Round hard, and say your assumptions out loud.

**The data.** A transaction row is a few small columns: an id, an account id, an amount, a short description and a timestamp. Allow about 70 bytes per row once the database's own per-row overhead is included:

> 10,000,000 rows × 70 bytes ≈ 700 MB

The lab's table measured 674 MB, and its indexes 200 to 400 MB each. The whole thing fits in memory on an ordinary database server, so this is a query-and-index problem, not a size problem. That rules out sharding immediately.

**The traffic.** Suppose a million customers, each opening the app three times a day and viewing five screens:

> 1,000,000 × 3 × 5 = 15 million requests a day
> 15,000,000 ÷ 86,400 seconds ≈ 175 a second on average
> Peak, at roughly 10× average (month-end, lunchtime) ≈ 1,750 a second

In stages 3 and 4, one laptop served about that many, once each request was a single indexed query, with the database, the API and the load generator all sharing it. So a well-indexed single database server handles this.

**Growth.** At a few million new transactions a day, the table adds roughly a billion rows over a year, around 70 GB before indexes. That's when stage 7's partitioning and archiving start to matter: not today, but within the planning horizon.

**Latency, roughly.** Orders of magnitude worth knowing, not exact figures, so measure your own:

| Operation | Roughly |
|---|---|
| Read from memory | 100 ns |
| Random read from an SSD | 100 µs |
| Round trip within a data centre | 0.5 ms |
| Random read from a spinning disk | 10 ms |

An index lookup that touches 3 or 4 pages costs microseconds from memory. Reading all 10 million rows costs hundreds of milliseconds even from memory: in stage 1, 370 ms. That gap is what the answer has to close.

## 3. Design

Work up the ladder, cheapest first, and say what each step costs and when you'd stop.

1. **Measure** ([stage 1](./01-measure-first)). Latency percentiles for the endpoint; `pg_stat_statements` sorted by total time; `EXPLAIN (ANALYZE, BUFFERS)` on the top query with a busy account and a quiet one. In the lab, that showed one query taking 99.7% of database time, scanning all 10 million rows to return 20.
2. **Index for the whole query** ([stage 2](./02-indexes-and-query-shape)). A composite index on `(account_id, created_at DESC)`, built concurrently. In the lab, the busiest account's query went from 400 ms to 0.18 ms. Check the query shape too: no functions wrapped around indexed columns.
3. **Fix how the code queries** ([stage 3](./03-what-is-the-orm-doing)). One query per request instead of N+1, only the columns needed, and keyset paging so deep pages cost the same as the first. In the lab: 9 queries became 1, and throughput rose 3.8 times.
4. **Check connections and threads** ([stage 4](./04-too-many-connections)). If it's only slow at peak: size the pool from measurements, keep everything async, and set short connection timeouts so overload fails fast.
5. **Cache hot reads** ([stage 5](./05-cache-then-invalidate)), if the same answers are read far more often than they change, with invalidation after writes and stampede protection.
6. **Add read replicas** ([stage 6](./06-read-replicas)), if reads still exceed one machine, routing reads that tolerate lag to them, with read-your-writes for the customer who just wrote.
7. **Partition, and later shard** ([stage 7](./07-partitioning-and-sharding)), only when the data or the write rate outgrows one machine. For ten million rows, partitioning is about retention and time-range queries, not capacity, and sharding isn't on the table.

For this question, steps 1 to 3 almost certainly solve it. Say so, and say what would make you go further.

## 4. Deep dive

Interviewers push wherever your answer was thinnest. Practise these.

::: details "Why not just add a cache?"
Because it hides the problem without fixing it. The slow requests are probably for busy accounts and deep pages, which are exactly the ones a cache helps least: they're diverse, and they change whenever a transaction arrives. A cache also adds staleness and invalidation to manage. Fix the query first, then cache what's still hot.
:::

::: details "Why not move to a NoSQL database?"
Nothing here is beyond a relational database. Ten million rows fit in memory, and the access pattern, by account and newest first, is exactly what a composite B-tree index serves. Changing databases costs months and gives up transactions and ad-hoc queries, for a problem an index fixes in minutes.
:::

::: details "How do you add that index to a busy production table?"
`CREATE INDEX CONCURRENTLY`, so writes keep flowing, at a quiet time if possible. Check there's disk space, watch its progress, confirm it isn't left `INVALID`, and drop any index it makes redundant. In EF Core with Npgsql, `IsCreatedConcurrently()` makes the migration do this outside a transaction.
:::

::: details "It's slow for only a few customers. Why, and what do you do?"
Their accounts have far more transactions than most. In the lab, the busiest account had 4,000 times the median. A plan that's fine for 50 rows can be terrible for 200,000. Test plans with both, and design the index so the work doesn't depend on how many rows an account has: with `(account_id, created_at DESC)`, the newest 20 cost the same for everyone.
:::

::: details "Now the table is 10 billion rows. What changes?"
Size now matters: roughly 700 GB of rows plus indexes won't fit in memory, and maintenance, backups and restores get slow. Partition by time so recent data stays hot and old months can be archived or dropped cheaply, and keep indexes lean. If the write rate also outgrows one machine, split by function first, then consider sharding by account with consistent hashing, accepting cross-shard queries and transfers as new problems.
:::

::: details "You added replicas, and a customer says their payment disappeared."
Replication lag: their read went to a replica that hadn't applied the payment yet. Route a customer's reads to the primary until a replica has caught up with their last write, using a consistency token with the primary's log position, or a sticky window. And never use a replica for the balance check that approves a payment.
:::

## The answer in two minutes

> "Ten million rows isn't big data. The table is probably under a gigabyte and fits in memory, so I'd treat this as a query problem first. I'd start by measuring: which endpoint, which percentile, since when. Then I'd use `pg_stat_statements` to find the queries with the most total time, and `EXPLAIN ANALYZE` the top one with a busy account and a quiet one.
>
> The usual cause is a query scanning or sorting far more rows than it returns. I'd add a composite index that matches the filter and the sort, like `(account_id, created_at DESC)`, built concurrently, and make sure the query is SARGable. Then I'd check what the ORM sends: N+1 queries, over-fetching, and offset paging, which I'd replace with keyset paging.
>
> If it's only slow at peak, I'd look at queuing: pool size, blocking calls and timeouts. Only after that would I cache hot reads, add read replicas with read-your-writes, or partition by time for growth. Sharding isn't justified at this size. At each step I'd re-measure, and stop once p99 is back within target."

## What to review

If any part of your answer felt shaky, go back to its stage:

| If you were unsure about | Review |
|---|---|
| Where to start, percentiles, reading a plan | [1. Measure first](./01-measure-first) |
| Which index, column order, SARGable queries | [2. Indexes and query shape](./02-indexes-and-query-shape) |
| N+1, projections, keyset paging | [3. What is the ORM doing?](./03-what-is-the-orm-doing) |
| Slow only at peak, pools, timeouts | [4. Too many connections](./04-too-many-connections) |
| Caching and invalidation | [5. Cache it, then invalidate it](./05-cache-then-invalidate) |
| Replicas and lag | [6. Reads outgrow one box](./06-read-replicas) |
| Partitioning, sharding, hot keys | [7. Writes outgrow one box](./07-partitioning-and-sharding) |
