# Handbook samples

The labs for the backend journeys run against one small banking API: accounts and their transactions, stored in Postgres. Each stage's lab measures or changes this app.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker, with Docker Compose

## Start it

From this folder:

```sh
docker compose up -d                            # Postgres on localhost:5433
dotnet run --project src/Bank.Api -- seed       # schema plus 10M transactions, about a minute
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

To start again from scratch, run `docker compose down -v` (this deletes the data), then repeat the steps above.

To seed less data on a slower machine: `dotnet run --project src/Bank.Api -- seed --transactions 2000000`. The labs assume the default, so query plans and timings will differ from the pages.

## What you get

- **A starting point that works in development.** The API is correct, but it was written without thinking about scale. Journey B1 measures it and improves it one stage at a time.
- **Only primary keys are indexed.** Choosing the other indexes is part of B1 stage 2.
- **Realistic, deterministic data.** 100,000 accounts and 10 million transactions over two years. A few accounts are very busy: account 1 has about 2% of all transactions, and the busiest 1% of accounts have about 20%. The median account has around 50. The data is generated from a fixed seed, so everyone gets the same rows and the same query plans.

## Lab tools

- **Load generator.** `dotnet run --project tools/Bank.Load -- --seconds 20 --concurrency 8` sends requests for random accounts and reports latency percentiles. Use `--path` to target another endpoint, for example `--path "/accounts/{id}/transactions/feed"`.
- **Switching implementations.** The transactions endpoint has two versions you can measure against each other: `dotnet run --project src/Bank.Api -- --Lab:Transactions=Projected` (the default is `Naive`).
- **Seeing the SQL.** Add `--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information` to log every query EF runs, with its duration.
- **Query statistics.** Postgres records every query's call count and time in `pg_stat_statements`. Enable it once with `docker compose exec postgres psql -U bank -d bank -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements"`.

## Tests

```sh
dotnet test --solution Samples.sln
```

The integration tests start their own throwaway Postgres in Docker, so Docker must be running. They don't touch the lab database.
