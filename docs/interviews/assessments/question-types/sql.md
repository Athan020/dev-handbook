---
title: SQL questions
track: shared
status: written
---

# SQL questions

A SQL question gives you a schema, a little sample data and a question: "List each account with its number of transactions in the last 30 days, including accounts with none." Your query runs against a hidden dataset, and its result is compared with the expected one, row by row and column by column.

SQL questions fail for different reasons from coding questions. Speed rarely matters. What matters is NULLs, joins that silently drop rows, ties, and output that's nearly but not exactly right.

The playgrounds on this page run real Postgres in your browser, on a small version of the lab's bank database: `accounts` (5,000 rows) and `transactions` (250,000 rows). A transaction's `counterparty_account_id` is filled in only for transfers; for card payments, fees and the like, it's NULL. Run the playgrounds in order.

```sql playground
SELECT id, account_id, counterparty_account_id, amount, description, created_at
FROM transactions
ORDER BY created_at DESC
LIMIT 10;
```

## Stop and think

::: think Your query returns the right rows on the sample data, but fails the hidden tests. What would you check?

- **NULLs.** Does the hidden data have NULLs where the sample didn't? `NOT IN`, `=`, `COUNT(column)`, `AVG` and string concatenation all treat NULLs in ways that surprise people.
- **Rows that should appear with zero.** "Including accounts with none" needs an outer join, and a condition in the wrong place turns it back into an inner join.
- **Ties.** "The top 3" when the third and fourth are equal: does the question want 3 rows, or all of the tied ones?
- **Ordering.** Without `ORDER BY`, rows come back in whatever order the database likes, and it can differ between the sample and the hidden data.
- **Columns and formatting.** Column order and names, rounding, and date formats as the question specifies them.

:::

## Joins that keep the zeros

"Each account, with its number of transactions in the last 30 days, including accounts with none" needs every account, whether or not it has matching transactions. That's a `LEFT JOIN`, with the date condition in the `ON` clause, and `count(t.id)`, which counts only matched rows:

```sql playground
SELECT a.id, a.holder_name, count(t.id) AS transactions_last_30_days
FROM accounts a
LEFT JOIN transactions t
  ON t.account_id = a.id
 AND t.created_at >= now() - interval '30 days'
GROUP BY a.id, a.holder_name
ORDER BY transactions_last_30_days, a.id
LIMIT 10;
```

::::: trap
A candidate writes this for the same question. On the hidden data, no account appears with zero. Why?

```sql
SELECT a.id, count(t.id) AS transactions_last_30_days
FROM accounts a
LEFT JOIN transactions t ON t.account_id = a.id
WHERE t.created_at >= now() - interval '30 days'
GROUP BY a.id;
```

::: details Answer
The `WHERE` clause runs after the join. For an account with no recent transactions, the left join produces one row with every `t` column NULL, and `NULL >= ...` isn't true, so `WHERE` throws that row away. The account disappears, and the left join behaves exactly like an inner join.

Conditions on the right-hand table of a left join belong in `ON`. And count a column from that table, `count(t.id)`, not `count(*)`: `count(*)` counts the NULL-filled row, so an account with no transactions would show 1.
:::
:::::

## NULLs and NOT IN

"Which accounts haven't received a transfer in the last 7 days?" reads naturally as `NOT IN`:

::::: trap
This returns no rows at all, although thousands of accounts haven't received a transfer this week. Why?

```sql
SELECT id
FROM accounts
WHERE id NOT IN (
    SELECT counterparty_account_id
    FROM transactions
    WHERE created_at >= now() - interval '7 days'
);
```

::: details Answer
The subquery returns the counterparty of every transaction in the last week, and most transactions aren't transfers, so it contains NULLs. `id NOT IN (1, 2, NULL)` means `id <> 1 AND id <> 2 AND id <> NULL`, and `id <> NULL` is unknown, never true. So the whole condition is never true, for any account.

Use `NOT EXISTS`, which asks "is there a matching row?" and isn't affected by NULLs. Or filter the NULLs out of the subquery, but that's easy to forget next time.
:::
:::::

The fix, and the NULLs that caused the problem:

```sql playground
SELECT count(*) AS accounts_without_a_transfer_this_week
FROM accounts a
WHERE NOT EXISTS (
    SELECT 1
    FROM transactions t
    WHERE t.counterparty_account_id = a.id
      AND t.created_at >= now() - interval '7 days'
);

SELECT count(*) AS transactions_this_week,
       count(*) FILTER (WHERE counterparty_account_id IS NULL) AS with_no_counterparty
FROM transactions
WHERE created_at >= now() - interval '7 days';
```

Other NULL behaviour worth knowing:

- `count(*)` counts rows; `count(column)` counts non-NULL values. `avg`, `sum`, `min` and `max` ignore NULLs, so an average of NULLs and numbers is the average of the numbers only.
- `NULL = NULL` isn't true. Test with `IS NULL`, or `IS NOT DISTINCT FROM` to compare two values that may both be NULL.
- `coalesce(value, 0)` substitutes a default, which is often what "show 0 for accounts with none" needs.

## Grouping

`WHERE` filters rows before grouping; `HAVING` filters groups after it. Debits by type, for types with more than 10,000 of them:

```sql playground
SELECT description,
       count(*) AS transactions,
       sum(amount) AS total,
       round(avg(amount), 2) AS average
FROM transactions
WHERE amount < 0
GROUP BY description
HAVING count(*) > 10000
ORDER BY total;
```

Every column in the `SELECT` list must be grouped or aggregated. Some databases, such as MySQL in some configurations, accept a query that breaks this rule and return an arbitrary value. Don't rely on it.

## Window functions

Window functions compute across related rows without collapsing them into groups. Three come up again and again.

**Top n per group.** The three largest debits for each of the first three accounts. `rank()` gives tied rows the same rank, so a tie can return more than three rows; `row_number()` always returns exactly three, breaking ties arbitrarily unless you add a tiebreaker to its `ORDER BY`. Choose based on what the question says about ties.

```sql playground
SELECT account_id, id, amount, rank
FROM (
    SELECT account_id, id, amount,
           rank() OVER (PARTITION BY account_id ORDER BY amount) AS rank
    FROM transactions
    WHERE amount < 0 AND account_id <= 3
) ranked
WHERE rank <= 3
ORDER BY account_id, rank, id;
```

**Running totals.** A running balance for one account. Ordering by `created_at, id` makes the order unambiguous when two transactions share a timestamp:

```sql playground
SELECT id, created_at, amount,
       sum(amount) OVER (ORDER BY created_at, id) AS running_total
FROM transactions
WHERE account_id = 42
ORDER BY created_at, id
LIMIT 20;
```

**Comparing with the previous row.** Month-on-month change in one account's incoming money, with `lag()`. The first month has no previous month, so its change is NULL:

```sql playground
SELECT month, total, total - lag(total) OVER (ORDER BY month) AS change
FROM (
    SELECT date_trunc('month', created_at)::date AS month, sum(amount) AS total
    FROM transactions
    WHERE account_id = 1 AND amount > 0
    GROUP BY 1
) monthly
ORDER BY month;
```

A window function can't be used in `WHERE` directly, because `WHERE` runs first. Compute it in a subquery or a `WITH` clause, then filter outside, as the top-n query does.

## Dialects

Platforms usually let you choose the database, and the hidden tests run on the one you chose. Know the differences that matter for your choice:

| | PostgreSQL | MySQL | SQL Server | Oracle |
|---|---|---|---|---|
| First n rows | `LIMIT n` | `LIMIT n` | `TOP n`, or `OFFSET 0 ROWS FETCH NEXT n ROWS ONLY` | `FETCH FIRST n ROWS ONLY` |
| `5 / 2` | `2` (integer division) | `2.5000` | `2` (integer division) | `2.5` |
| Joining strings | `\|\|` or `concat()` | `concat()` | `+` or `concat()` | `\|\|` or `concat()` |
| NULLs in ascending order | Last | First | First | Last |
| Start of the month | `date_trunc('month', d)` | `DATE_FORMAT(d, '%Y-%m-01')` | `DATETRUNC(month, d)`, from SQL Server 2022 | `TRUNC(d, 'MM')` |

Integer division is the one that costs points most often: an average or percentage computed from two integer counts is silently truncated in PostgreSQL and SQL Server. Multiply by `1.0` first, as in `100.0 * part / total`.

## Practise

[Practice set 3](../practice/#set-3-sql) has four SQL questions with visible and hidden datasets, run against Postgres on your machine.
