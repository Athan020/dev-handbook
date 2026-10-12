---
title: Practice sets
track: shared
status: written
---

# Practice sets

Four timed sets, built to behave like a real assessment. Each has a few visible example tests and more hidden tests, including large inputs with a time limit, so a slow solution fails the way it would on a platform. You run them on your own machine with `dotnet test`.

| Set | Time | What it practises |
|---|---|---|
| [1. Warm-up](#set-1-warm-up) | 60 min | Edge cases, overflow, reading the statement precisely |
| [2. Large inputs](#set-2-large-inputs) | 75 min | Choosing the complexity the constraints demand |
| [3. SQL](#set-3-sql) | 45 min | NULLs, outer joins, ties, window functions |
| [4. Project task](#set-4-project-task) | 60 min | Fixing bugs and adding a feature in an unfamiliar codebase |

## How to use them

You need the .NET SDK, and Docker for the SQL set. The sets are in `samples/assessments/`, and each one has the same layout:

- `Problems/`: where you write your answers. Each file has the signature to complete.
- `Tests/`: the tests. Read the visible ones; the hidden ones are there too, but you'll learn more if you don't read them first.
- `Reference/`: worked solutions, with comments on the approach. Don't open them until you've finished.

Set a timer, read every question first, and work as you would on the day. From `samples/assessments`:

```sh
dotnet test --project Set1.Warmup --filter-trait "Tests=Visible"   # only the examples, as a platform shows them
dotnet test --project Set1.Warmup                                  # everything, as a platform scores it
```

When the time is up, run everything, and count the hidden tests you passed. Then read the worked solutions, and try again later any problem you didn't finish. To see the worked solutions pass, add `-p:UseReference=true`.

Score yourself by points: a problem's points are shared equally among its tests. [Test-day strategy](../test-day-strategy) explains why that matters.

## Set 1: Warm-up

60 minutes, three problems, in `Set1.Warmup/Problems/`. Amounts are in cents.

### Problem 1: Overdraft (100 points)

An account starts with `openingBalance`. Transactions are applied in order: positive amounts are deposits, negative amounts withdrawals. The account may go below zero, but not below `-overdraftLimit`. Return the index of the first transaction that takes the balance below `-overdraftLimit`, or -1 if none does.

- 0 ≤ number of transactions ≤ 100,000
- 0 ≤ `openingBalance`, `overdraftLimit` ≤ 10¹²; each amount is between -10¹² and 10¹²

Example: opening balance 10,000, limit 5,000, amounts `[-8,000, -6,000, -2,000, 50,000]` gives 2. After the third transaction, the balance is -6,000.

### Problem 2: Duplicate payments (200 points)

Payments arrive in time order, each with an id, a payer, a payee, an amount and a timestamp in seconds. A payment is a suspected duplicate if the most recent earlier payment with the same payer, payee and amount was made at most 60 seconds before it. Return the ids of the suspected duplicates, in input order.

- 0 ≤ number of payments ≤ 200,000; timestamps never decrease
- Payer and payee are compared exactly, and are not interchangeable

Example: p1 (alice → bob, 2,500, at 1,000), p2 (alice → bob, 2,500, at 1,030), p3 (alice → carol, 2,500, at 1,040), p4 (alice → bob, 2,500, at 1,200) gives `[p2]`.

### Problem 3: Check an IBAN (300 points)

Return whether a string is a valid IBAN, by these rules:

1. Ignore spaces, and treat letters as upper case.
2. What remains must be 15 to 34 characters long: two letters (the country), two digits (the check digits), then letters and digits only.
3. Move the first four characters to the end. Replace each letter with two digits: A = 10, B = 11, and so on to Z = 35.
4. The resulting number, taken modulo 97, must equal 1.

Don't check country-specific lengths. Strings are at most 100 characters long.

Example: `GB85TEST12345612345678` is valid; `GB86TEST12345612345678` isn't.

## Set 2: Large inputs

75 minutes, three problems, in `Set2.LargeInputs/Problems/`. Every problem has a simple solution that passes the small tests and runs out of time on the large ones. Read the constraints first: see [reading constraints](../reading-constraints).

### Problem 1: The busiest window (100 points)

Given transaction timestamps in seconds, sorted in ascending order, and a window length `w`, return the largest number of transactions that fall in any window from some time t to t + w, both ends included.

- 0 ≤ n ≤ 200,000; 0 ≤ timestamps ≤ 10¹³; 0 ≤ w ≤ 10⁹; timestamps may repeat

Example: timestamps `[0, 10, 50, 55, 59, 200]` and w = 10 gives 3: the window from 50 to 60.

### Problem 2: Range totals (200 points)

Given each day's net total, and a list of questions "what is the total from day `From` to day `To`, inclusive?" (days numbered from 0), return the answers in order.

- 1 ≤ days ≤ 200,000; each day's total is between -10⁹ and 10⁹
- 0 ≤ questions ≤ 200,000; 0 ≤ `From` ≤ `To` < days

Example: days `[10, 20, 30, -35]` and questions (0, 2), (0, 0), (1, 3) give `[60, 10, 15]`.

### Problem 3: Enough tellers (300 points)

Each customer's visit runs from `Start` (inclusive) to `End` (exclusive). Return the smallest number of tellers needed so that no customer waits. A teller who finishes at time t can serve a customer who arrives at t. Visits are in no particular order.

- 0 ≤ visits ≤ 200,000; 0 ≤ `Start` < `End` ≤ 10¹²

Example: visits (0, 30), (10, 20), (25, 40), (35, 50) need 2 tellers.

## Set 3: SQL

45 minutes, four questions. Write each query in its file in `Set3.Sql/Problems/`, for PostgreSQL. The tests run your queries against two databases: the sample data below, and hidden data with the same schema. Rows must come back in the order asked for, with the columns in the order listed. This set needs Docker running.

The schema:

<<< @/../samples/assessments/Set3.Sql/Data/schema.sql

The sample data:

<<< @/../samples/assessments/Set3.Sql/Data/visible.sql

### Question 1: Open accounts by branch

For each branch, the number of open accounts (those with no `closed_on` date). Include branches with no open accounts. Columns: `branch_name`, `open_accounts`. Order by `open_accounts`, largest first, then by `branch_name`.

On the sample data: Durban 2, Cape Town 1, Pretoria 0.

### Question 2: Never received money

The ids of accounts that have never been the counterparty of any transaction. Column: `account_id`. Order by `account_id`.

On the sample data: 4, 5.

### Question 3: Largest debit

For each account with at least one debit (a negative amount), its largest debit, meaning the most negative, and when it happened. If several debits are equally large, show the earliest. Columns: `account_id`, `amount`, `created_at`. Order by `account_id`.

On the sample data: account 1, -500.00 at 2026-01-10 10:00:00; account 3, -50.00 at 2026-02-03 12:00:00.

### Question 4: Monthly change

For each account, its net total for each month of 2026 in which it had transactions, and the change from its previous month with transactions in 2026. The first such month's change is NULL. Columns: `account_id`, `month` (a `date`: the first day of the month), `net`, `change`. Order by `account_id`, then `month`.

On the sample data, the first two rows are: account 1, 2026-01-01, -700.00, NULL; account 1, 2026-02-01, -500.00, 200.00.

## Set 4: Project task

60 minutes, in `Set4.Project/`. A small fee-calculation codebase, with two bugs reported by customers and a feature to add. The task is in `Set4.Project/README.md`, and the codebase is in `Problems/`. Read the README when you start the timer, as you would the task on a platform.

Work as [project tasks](../question-types/projects) describes: run the tests first, read the failing ones, and make small changes. The visible tests are the ones you'd find in the repository; the hidden ones check boundaries the task describes but the visible tests don't.
