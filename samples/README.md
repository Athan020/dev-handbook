# Handbook samples

The labs for the backend journeys run against one small banking API: accounts and their transactions, stored in Postgres. Each stage's lab measures or changes this app.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker, with Docker Compose

## Start it

From this folder:

```sh
docker compose up -d                            # Postgres on localhost:5433
dotnet run --project src/Bank.Api -- seed       # schema plus 10M transactions, one to three minutes
dotnet run --project src/Bank.Api               # API on http://localhost:5080
```

Then try it:

```sh
curl "http://localhost:5080/accounts/1"
curl "http://localhost:5080/accounts/1/transactions?page=1&pageSize=20"
```

The endpoints:

| Endpoint | What it returns |
|---|---|
| `GET /accounts/{id}` | One account |
| `GET /accounts/{id}/transactions?page=1&pageSize=20` | Newest transactions first, by page number (offset paging) |
| `GET /accounts/{id}/transactions/feed?pageSize=20&cursor=…` | The same, by cursor (keyset paging); pass each response's `nextCursor` to get the next page |
| `GET /accounts/{id}/summary` | Balance, transaction count and last activity, computed from the whole history |
| `POST /accounts/{id}/transactions` | Records a transaction, e.g. `{"amount": -100.00, "description": "Card payment"}` (for the labs, not a real transfer) |
| `POST /transfers` | Moves money between two accounts, e.g. `{"fromAccountId": 1, "toAccountId": 2, "amount": 50.00, "reference": "Rent"}`, then does follow-up work (journey B2) |

To start again from scratch, run `docker compose down -v` (this deletes the data), then repeat the steps above.

To seed less data on a slower machine: `dotnet run --project src/Bank.Api -- seed --transactions 2000000`. The labs assume the default, so query plans and timings will differ from the pages.

## What you get

- **A starting point that works in development.** The API is correct, but it was written without thinking about scale. Journey B1 measures it and improves it one stage at a time.
- **Only primary keys are indexed.** Choosing the other indexes is part of B1 stage 2.
- **Realistic, deterministic data.** 100,000 accounts and 10 million transactions over two years. A few accounts are very busy: account 1 has about 2% of all transactions, and the busiest 1% of accounts have about 20%. The median account has around 50. The data is generated from a fixed seed, so everyone gets the same rows and the same query plans.

## Journey B2: messaging

Journey B2 adds RabbitMQ (started by `docker compose up -d`; management UI at http://localhost:15672, user `bank`, password `bank`) and two more processes. Run each in its own terminal:

```sh
dotnet run --project tools/Bank.FakeServices   # stand-in email and fraud services on port 5090
dotnet run --project src/Bank.Worker           # does follow-up work taken off RabbitMQ
```

`--Lab:Transfers=Inline|Background|Queue|Event|Outbox` on the API chooses where a transfer's follow-up work runs, and how its event is published (default `Inline`). `--Lab:CrashAfterCommit=0.1` and `--Lab:RelayCrashChance=0.2` make the API die at the worst moments, for stage 4.

`--Worker:Role=follow-up|email|fraud|rewards|history|statements|replay` chooses what a worker does. `--Worker:Idempotent=true` makes the email and rewards workers safe against duplicates, and `--Worker:CrashChance=0.1` makes a worker die after the work but before acknowledging it, for stage 3. For stage 5, `POST /accounts` opens an account, `--Worker:Topology=Shared|Partitioned` chooses how the statements worker reads account events, and `dotnet run --project tools/Bank.Load -- --ordering-burst 150` creates tightly packed events. For stage 6, `--Worker:Retries=Requeue|Backoff` chooses what happens to failed messages, and `--Worker:Role=replay --Worker:Queue=<queue>` moves a queue's dead letters back. For stage 7, `--Worker:Concurrency` and `--Worker:Prefetch` set how many messages a worker handles at once, `"maxConcurrentEmails": 20` in the fake services' settings makes the email provider refuse (429) calls beyond 20 at a time, and `dotnet run --project tools/Bank.Load -- --payroll 6000 --concurrency 32` sends a burst of salary transfers. After pulling new code, run `dotnet run --project src/Bank.Api -- seed` once: it applies any new database migrations, then stops when it sees existing data. Make the fake services slow or unreliable while they run, and see what they've done:

```sh
curl -X POST localhost:5090/settings -H "Content-Type: application/json" -d '{"emailDelayMs": 300, "fraudDelayMs": 500, "failureRate": 0.2}'
curl localhost:5090/stats   # emails sent, duplicates suppressed, failed and refused (rateLimited) calls, email lag
curl -X POST localhost:5090/reset
```

## Lab tools

- **Load generator.** `dotnet run --project tools/Bank.Load -- --seconds 20 --concurrency 8` sends requests for random accounts and reports latency percentiles. Use `--path` to target another endpoint, for example `--path "/accounts/{id}/transactions/feed"`.
- **Transfers under load.** `dotnet run --project tools/Bank.Load -- --transfers` posts transfers between random accounts instead of reading.
- **Hot accounts.** Add `--hot 0.8 --hot-accounts 100` to the load generator to send 80% of requests to the 100 busiest accounts.
- **Switching implementations.** Several endpoints have versions you can measure against each other, chosen at startup:
  - `--Lab:Transactions=Naive|Projected|Blocking` for the transactions page (default `Naive`)
  - `--Lab:Summary=None|CacheAside|Hybrid` and `--Lab:CacheSeconds=30` for the summary (default `None`)
  - `"--ConnectionStrings:Bank=Host=localhost;Port=5433;Database=bank;Username=bank;Password=bank;Maximum Pool Size=10"` to change the connection pool
- **A read replica.** `docker compose --profile replica up -d` starts a streaming replica on port 5434 (it clones the primary on first start). `REPLICA_DELAY=2000 docker compose --profile replica up -d` makes it apply changes 2 seconds late. Route reads with `--Lab:Reads=Primary|Replica|ReadYourWrites`; responses say who served them in `X-Served-By`, and writes return an `X-Consistency-Token`.
- **A second instance.** `dotnet run --project src/Bank.Api --no-launch-profile -- --urls http://localhost:5081` runs another copy on port 5081.
- **Seeing the SQL.** Add `--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information` to log every query EF runs, with its duration.
- **Query statistics.** Postgres records every query's call count and time in `pg_stat_statements`. Enable it once with `docker compose exec postgres psql -U bank -d bank -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements"`.

## Tests

```sh
dotnet test --solution Samples.sln
```

The integration tests start their own throwaway Postgres in Docker, so Docker must be running. They don't touch the lab database.

## Assessment practice

`assessments/` is a separate solution of timed practice sets for online assessments, plus the tested snippets from the C# toolkit page. See [assessments/README.md](assessments/README.md).
