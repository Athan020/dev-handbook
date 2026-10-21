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

## Journey B3: money that adds up

B3 extends the same transfer API, and its labs run with `--Lab:Transfers=Event` (or `Outbox`). `--Lab:Funds=None|CheckSum|Conditional|ForUpdate|Optimistic` chooses how a transfer checks the account can afford it (default `None`, as in B1 and B2; the last three use the stored `accounts.balance`, which deposits also keep up to date); `--Lab:Isolation=ReadCommitted|RepeatableRead|Serializable` sets the transaction's isolation level; and `--Lab:Retries=10` retries transfers that fail with a serialization failure or deadlock. Refused transfers return 422, and transfers that lost a race and ran out of retries return 409. `dotnet run --project tools/Bank.Load -- --race 10` opens accounts with a deposit of 100 and sends ten transfers of 60 from each at once (`--rounds`, `--deposit` and `--amount` change the numbers). For stage 2, `--Lab:LockOrder=Request|AccountId` sets the order in which a transfer locks its two accounts, `--Lab:LockLate=true` changes the balances after writing everything else, `--payroll 2000 --employer` sends every salary from one new account, and `--crossfire 200` sends transfers both ways between two new accounts. For stage 3, `--Lab:Idempotency=None|CheckFirst|Keys` chooses how the API treats the `Idempotency-Key` header, and `--double-pay 1000` makes payments the way a phone does: retrying after `--timeout-ms` (default 1000) and double-tapping every tenth payment, with `--keys` to send keys. For stage 6, `POST /outgoing-payments` pays another bank through a fake payment network in `Bank.FakeServices` (`/network/settings` sets `lostReplyRate`, `failureRate` and `deduplicates`); `--Lab:Outgoing=Naive|Pending` chooses how the API copes with unknown outcomes, `--Lab:Resolve=ResendBlindly` makes the resolver resend without asking, and `--outgoing 500` compares what customers were charged with what the network paid. For stage 7, the network's `amountErrorRate` and `missingFromRecordsRate` add mistakes of its own, and `POST /reconciliations?fromAccountId=<id>` matches outgoing payments against the network's settlement record and classifies every difference. The stage 1 check needs B1 stage 3's index; the stage page has the command.

## Journey B4: things fail

B4 adds a fraud decision before every transfer. The fake services' fraud service (`POST /fraud/settings`) can be slow (`delayMs`), hang (`"mode": "hang"`), be down (`"mode": "down"`), fail at random (`failureRate`), or have a fixed `capacity` and `queueLimit`; `POST /fraud/blip?seconds=5` takes it down briefly. `--Lab:FraudGate=Off|InTransaction|BeforeTransaction` sets where the API calls it, and the `Fraud` settings switch on each resilience mechanism: `--Fraud:TimeoutMs`, `--Fraud:Retries` (with `RetryDelayMs`, `Backoff=Constant|Exponential` and `Jitter`), `--Fraud:Breaker=true` (with `BreakerFailureRatio`, `BreakerMinimumThroughput`, `BreakerSamplingSeconds` and `BreakerBreakSeconds`) and `--Fraud:MaxConcurrent` (with `QueueLimit`). `--Lab:RequestTimeoutMs` gives every request a deadline. For stage 2, `--Fraud:RetryBudgetPercent` caps retries at a share of recent calls, and the fraud service's `slowMs`, `capacity`, `queueLimit` and `finishesAbandonedWork` (default true) shape how it behaves under load (`POST /fraud/blip?seconds=3&kind=slow`). For stage 4, `--Lab:SharedOutboundPool=20` sends email and fraud calls through one 20-connection pool, `--Lab:FollowUpConcurrency=50` runs 50 background follow-up workers, and `--Lab:FollowUpMaxConcurrent=10` is a bulkhead on that work. For stage 5, `--Fraud:Fallback=FailClosed|FailOpen|AllowSmall` (with `--Fraud:FallbackLimit=1000`) decides what a transfer does without a fraud decision; held transfers are made by a background job once the fraud service is back, and `GET /fraud-reviews/summary` counts unchecked, held, released and declined transfers. For stage 6, `--Lab:RateLimit=None|Global|PerClient` limits callers by their `X-Client-Id` header, with `--Lab:RateLimitAlgorithm=TokenBucket|FixedWindow|SlidingWindow`, `--Lab:RateLimitPerSecond` (default 50) and `--Lab:RateLimitBurst` (default 100); `--Lab:RateLimitStore=Redis` shares the limit across instances (start Redis with `docker compose --profile redis up -d`). `dotnet run --project tools/Bank.Load -- --noisy --seconds 30` sends customers' transfers while one partner floods the API, optionally across `--targets http://localhost:5080,http://localhost:5081`. Two API instances need smaller connection pools (`Maximum Pool Size=40`), or together they exceed Postgres's limit of 100 connections. `GET /resilience/fraud` shows what the gate did. `dotnet run --project tools/Bank.Load -- --outage --seconds 40 --concurrency 150` sends transfers while a bystander reads balances, optionally with `--blip-at 10 --blip-seconds 5`, and prints a timeline; `--rate 150` sends 150 transfers a second whether or not earlier ones have been answered, and `--blip-kind slow` makes the blip slow rather than down.

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

## Journey B5: one service becomes many

B5's labs use a modular version of the bank, `src/Bank.Modular` (on http://localhost:5100), in the same Postgres database as the API but in schemas of its own. It has three modules, each a project of its own: the ledger (accounts, balances, transfers and the entries they post), payees, and statements. Modules reference only `src/Bank.Modular.Contracts`, so the compiler stops one using another's internals, and `tests/Bank.Modular.Tests` checks the rest: that no module references another, makes anything public beyond the class that switches it on, or maps a table outside its own schema. On first start, each module creates its tables and seeds itself: 1,000 accounts with R10,000 each (`--Modular:SeedAccounts` to change it), and three payees per account.

| Endpoint | What it does |
|---|---|
| `GET /accounts/{id}` | An account and its balance (ledger) |
| `GET /accounts/{id}/payees`, `POST /accounts/{id}/payees`, `DELETE /accounts/{id}/payees/{payeeId}` | A customer's saved payees (payees), e.g. `{"payeeAccountId": 7, "name": "Landlord", "reference": "Rent"}` |
| `POST /transfers` | Pays a saved payee, e.g. `{"fromAccountId": 1, "payeeId": "…", "amount": 25.50}` (ledger, asking payees) |
| `GET /accounts/{id}/statements/{year}/{month}` | A monthly statement (statements, asking the ledger) |
| `GET /ledger/feed?after=0&max=500` | Every ledger entry in the order posted, after a position |
| `GET /ledger/check` | Whether the books add up: the total against the opening balances, and how many accounts disagree with their own entries |

For stage 2, `--Lab:EntityServices=true` adds the ledger split by table, as an Account service and a Transaction service would offer it (`GET /entity/accounts/{id}`, `PUT /entity/accounts/{id}/balance`, `POST /entity/entries`). `dotnet run --project tools/Bank.Load -- --split entity|capability --seconds 20` sends transfers through it or through `POST /transfers`, and checks the books before and after; `--crash-rate 0.01` makes the caller stop after the debit in that share of entity-split transfers. `dotnet run --project tools/Bank.Load -- --co-change src/Bank.Api` lists the files that most often change in the same commit. To start the books again, stop the app and run `docker compose exec postgres psql -U bank -d bank -c "DROP SCHEMA ledger CASCADE; DROP SCHEMA statements CASCADE"`; the modules recreate and reseed their schemas on the next start.

For stage 3, `--Lab:Statements=Call|Copy` decides whether statements ask the ledger for each statement, or keep their own copy of its entries by following `GET /ledger/feed` every `--Statements:FeedIntervalMs` (default 1000); `GET /statements/copy` shows how far the copy has got, and each statement says when it was complete to (`asOf`). Modules can also run in separate processes: `--Modules:Run=Ledger,Payees` runs only those two, and another process with `--urls http://localhost:5101 --Modules:Run=Statements --Modules:LedgerUrl=http://localhost:5100` reaches the ledger over HTTP through the same contract (`--Modules:PayeesUrl` likewise; `--Modules:TimeoutMs`, default 2000). A module that is down gives a 503. `dotnet run --project tools/Bank.Load -- --staleness --count 200 --rate 10` times how long each transfer takes to appear on the payer's statement (`--statements-url` if statements run elsewhere).

For stage 4, `--Transfers:Checks=customers,limits,fraud` makes every transfer ask those checks first, in the fake services' `/checks/{name}` (which pass after about 25 ms, are slow or fail as `POST /checks/settings` says, and go down with `POST /checks/fraud/blip?seconds=10`). `--Lab:Checks=Sequential|Parallel` decides how they're called, and `--Transfers:CheckRetries=1` retries a failed check once. `--Lab:Transfers=Accept` accepts transfers with 202 and makes them in the background, retrying while a dependency is down; `GET /transfers/{id}` shows each one's status. `dotnet run --project tools/Bank.Load -- --chain --seconds 60 --rate 20` sends transfers at a steady rate and follows accepted ones until they're made, with `--blip fraud --blip-at 20 --blip-seconds 10` to take a check down part-way, and `--payees-url` if payees run in another process. Several processes need smaller connection pools (`Maximum Pool Size=40` in `--ConnectionStrings:Modular`), or together they exceed Postgres's limit of 100 connections.

For stage 5, `POST /card-top-ups` (`{"accountId": 1, "cardId": "card-1", "amount": 50}`) moves money from an account onto a prepaid card held by the fake services' cards service (`/cards`, which applies each reference once, declines cards whose id starts with `blocked`, and fails or loses replies as `POST /cards/settings` says). `--Lab:TopUps=Naive|InMemory|Saga` decides how: with no compensation, with compensation run from memory, or as a saga whose state is saved with each step and carried forward by an orchestrator. `--Lab:CrashRate=0.02` simulates the process dying in that share of top-ups. The ledger's own accounts `-1` (card top-ups in transit) and `-2` (card funding) hold the money on its way. `dotnet run --project tools/Bank.Load -- --top-ups 500 --rate 25` sends top-ups and reconciles each one against the cards service's record. To start the ledger again from scratch, drop the statements schema too, since its copy remembers the ledger's positions: `DROP SCHEMA ledger CASCADE; DROP SCHEMA statements CASCADE`.

For stage 6, `dotnet run --project src/Bank.Gateway` starts a gateway on http://localhost:5110, built with YARP. It expects the ledger on 5100, payees on 5101 and statements on 5102 (run each with `--Modules:Run=...`, as in stage 3; addresses are in its `appsettings.json` under `ReverseProxy:Clusters`). It refuses requests without a valid token, refuses a customer another customer's account, limits each customer's rate (`--Gateway:RatePerSecond`, `--Gateway:Burst`), and passes the verified account on as `X-Customer-Account`. `POST /lab/token?accountId=7` issues a token for the lab; a real system gets tokens from an identity provider. `GET /app/home` is the mobile app's backend for frontend. `dotnet run --project tools/Bank.Load -- --home calls|bff --rtt 150` loads the home screen call by call (add `--parallel`, or `--direct` to skip the gateway) or with one call, adding 150 ms to each request as a mobile network would.

## Tests

```sh
dotnet test --solution Samples.sln
```

The integration tests start their own throwaway Postgres in Docker, so Docker must be running. They don't touch the lab database.

## Assessment practice

`assessments/` is a separate solution of timed practice sets for online assessments, plus the tested snippets from the C# toolkit page. See [assessments/README.md](assessments/README.md).
