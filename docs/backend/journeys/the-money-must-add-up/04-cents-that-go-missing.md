---
title: Cents that go missing
track: backend
journey: the-money-must-add-up
stage: 4
level: intermediate
sources: []
status: written
---

# 4. Cents that go missing

## Where we are

Payments are now safe to retry. Product adds a fee: 0.5% of every payment, rounded to the cent, and shared out 50% to the bank, 30% to a partner and 20% to a tax account. Each share is worked out the obvious way, as a percentage of the fee, rounded to the cent.

At month-end, finance adds up the three shares and compares them with the fees charged. The shares come to more than the fees. Then finance works out the fee on the month's total payments, and gets a third number.

## Stop and think

::: think Every fee was rounded correctly, and every share was rounded correctly. How can the shares add up to more than the fees?

Because rounding each share separately doesn't preserve their sum. A fee of 0.03 splits into exact shares of 0.015, 0.009 and 0.006, which round to 0.02, 0.01 and 0.01: 0.04 paid out for 0.03 charged. Each rounding is correct on its own; together they invent a cent.

On the playground's data below, 64,250 of 212,602 fees didn't match their shares, and the shares came to R642.50 more than the fees. Rounding errors don't cancel out when the same rule rounds the same kind of number, over and over.

:::

::: think Finance takes 0.5% of the month's total payments. The system took 0.5% of each payment and rounded it. They differ by R7.40. Who's right?

Both, by their own rule: they round at different points. Rounding 212,602 small fees separately isn't the same as rounding one big fee once. Neither is a bug, but the business has to decide which rule is the real one, usually what the customer agreement says ("0.5% of each payment, rounded to the nearest cent"), and every report and reconciliation must use the same rule.

:::

::: think Why not store money as a `double`? Isn't it precise enough?

A `double` stores numbers in binary, and most decimal fractions, such as 0.1 or 0.01, have no exact binary form. Each value is very slightly off, and the errors grow as values are added. In C#, adding 0.01 a million times as a `double` doesn't give 10,000. On the playground's data, summing 250,000 amounts as floating point gives 153,351,236.3500041 instead of 153,351,236.35. Tiny, but it's no longer the right answer, and comparisons for equality start failing.

:::

## The mental model

### Money is an amount and a currency

- **The amount is exact.** Use a decimal type: `decimal` in C#, `numeric` in PostgreSQL, or a whole number of the currency's smallest unit, such as cents in a `long` or `bigint`.
- **The currency travels with it.** Adding rand to yen is a bug, and a type that carries its currency can refuse it.
- **Each currency has its own decimal places.** Most have two; the yen has none; the Kuwaiti dinar has three. [ISO 4217](https://www.iso.org/iso-4217-currency-codes.html) defines them. Don't assume two.

<<< @/../samples/src/Bank.Money/Money.cs#currency

<<< @/../samples/src/Bank.Money/Money.cs#money

### Rounding is a business rule

Any multiplication or division, such as a percentage fee, interest, tax or currency conversion, produces more decimal places than the currency allows, so something must round. Two decisions make a rounding rule:

- **How.** Half up (away from zero), half to even ("banker's rounding"), always down. C#'s `Math.Round` defaults to half to even; PostgreSQL's `round(numeric)` rounds half away from zero. If the code doesn't say, two systems can disagree.
- **Where.** Per line, per invoice, per month. Each gives a different total.

The agreement, the tariff or the regulator decides both. The code's job is to make them explicit, and to use the same rule everywhere that calculates the same thing.

### Splitting must add up

When one amount is shared out, the parts must add up exactly to the whole, so rounding can't be applied to each part independently. Work in the currency's smallest unit: give each part its share rounded down, then hand the cents left over to the parts that lost the most to rounding (the *largest remainder* method), with a fixed rule for ties.

<<< @/../samples/src/Bank.Money/Money.cs#allocate

R100 split three ways is R33.34, R33.33 and R33.33. Someone has to get the extra cent; the method decides who, the same way every time.

## The escalation ladder

### 1. Use exact decimal types

`decimal` in C#, `numeric` in the database, never `float`, `double` or `real`.

**Fixes:** drift from binary fractions.
**Costs:** `decimal` arithmetic is slower than `double`. That rarely matters for money.
**Breaks:** nothing. Watch the boundaries: JSON numbers are usually parsed as `double` (JavaScript has no other number type), so send amounts as strings or as whole minor units. And avoid PostgreSQL's `money` type: its formatting depends on a server setting.

### 2. A money type

An amount with its currency, which refuses more decimal places than the currency has, and refuses to add different currencies.

**Fixes:** mixed currencies, and values rounded nowhere in particular.
**Costs:** a small type, and converting at the edges.
**Breaks:** nothing.

### 3. Explicit rounding, in one place

Every operation that produces extra decimal places takes the rounding rule as an argument (`Times(0.005m, MidpointRounding.AwayFromZero)`), and the rule comes from one place in the code.

**Fixes:** systems that disagree because each rounded its own way.
**Costs:** discipline.
**Breaks:** nothing, but it doesn't make split amounts add up.

### 4. Give the last share the remainder

Round every share but the last, and give the last share whatever is left: `tax = fee - bank - partner`.

**Fixes:** the sum. On the playground's data, the shares match the fees exactly.
**Costs:** nothing to compute.
**Breaks:** fairness. The last party absorbs every rounding difference, always in the same direction: the tax account received R270,277.58 instead of its 20% share of R270,917.17, R639.59 short.

### 5. Allocate by largest remainder

**Fixes:** the sum and the fairness. Every part is within a cent of its exact share. On the playground's data, the tax account received R270,920.08, within a few rand of 20% of the total; the difference comes from rounding each fee, not from the split.
**Costs:** a little more code, and a tie-breaking rule that has to be agreed.
**Breaks:** nothing.

### 6. Record the difference you can't avoid

Some calculations legitimately disagree, such as a fee on each payment against the same fee on the month's total, or a currency conversion in each direction. Don't let the difference vanish: post it to an account set aside for rounding differences, so the books still balance and the difference is visible. Stage 5 builds the ledger that makes that possible.

## Trade-offs at a glance

| Approach | Adds up | Fair | Costs |
|---|---|---|---|
| `double` | No: drifts | — | — |
| Round each share | No: R642.50 over on 212,602 fees | — | — |
| Last share takes the remainder | Yes | No: one party absorbs every difference | Nothing |
| Largest remainder | Yes | Yes: each part within a cent | A tie-breaking rule |
| A rounding-difference account | Makes unavoidable differences visible | — | A ledger (stage 5) |

## Traps

- **`double` or `float` for money**, anywhere: in code, in the database, in JSON.
- **Rounding without saying how.** C# and PostgreSQL round halves differently by default.
- **Rounding each part of a split.** The parts stop adding up.
- **Assuming two decimal places.** The yen has none, and some currencies have three.
- **Comparing totals rounded at different points** and calling the difference a bug, or, worse, not noticing it.

::::: trap
The fee splitter from this stage's trigger. It passed its tests, which used fees of 0.05, 0.07 and 0.11. What goes wrong?

```csharp
public static class FeeSplit
{
    // The fee goes 50% to the bank, 30% to the partner and 20% to the tax account.
    public static decimal[] Split(decimal fee) =>
    [
        Math.Round(fee * 0.5m, 2),
        Math.Round(fee * 0.3m, 2),
        Math.Round(fee * 0.2m, 2),
    ];
}
```

::: details Answer
The parts don't always add up to the fee. For 0.03, the exact shares are 0.015, 0.009 and 0.006, which round to 0.02, 0.01 and 0.01: 0.04. The test values happen to work.

It also rounds without saying how: `Math.Round` uses banker's rounding by default, so 0.015 rounds to 0.02 but 0.025 also rounds to 0.02. That may or may not match the agreement, and the database, rounding the same numbers half up, would disagree. And it assumes two decimal places.

Allocate the fee instead: work in cents, give each share its rounded-down part, and hand out the leftover cents by largest remainder, as `Money.Allocate` does. Then test it with many generated amounts, not three hand-picked ones.
:::
:::::

## In the interview

::::: interview
**Usually asked as:** "How would you represent money in your system?" Or: "How do you split a payment between several parties?"

**Short answer:** "As an exact decimal with its currency, never floating point: `decimal` in C#, `numeric` in the database, or whole minor units, and strings or minor units in JSON. Each currency's decimal places come from ISO 4217, so I don't assume two. Anything that produces extra decimals, like a percentage, rounds explicitly, with the rule and the point of rounding set by the business. And when an amount is split, I allocate it so the parts add up exactly: shares rounded down in minor units, then the leftover units by largest remainder, with a fixed tie-break. Any unavoidable rounding difference gets posted to its own account, so it's visible."

::: details Follow-up: integer cents or a decimal type?
Both work. Integer minor units are exact and fast, but every piece of code must know each currency's exponent, and percentages still need rounding. A decimal type keeps amounts readable and handles any number of decimal places, at some speed cost. Pick one and use it consistently, especially at boundaries such as JSON and the database.
:::

::: details Follow-up: how do you convert between currencies?
Multiply by a rate with enough precision, then round to the target currency's decimal places with an explicit rule, and record the rate used. Converting back won't give exactly the original amount, so don't try to make it: record the difference as an exchange gain or loss in its own account.
:::

::: details Follow-up: interest is calculated daily but paid monthly. Where do you round?
Wherever the product terms say, and that has to be decided explicitly. A common approach is to accumulate the daily interest at higher precision and round once when it's paid, so the daily roundings don't add up to a visible error. Whatever the rule, use the same one in every system that calculates it, so they reconcile.
:::
:::::

## Try it

These run in your browser against the playground's 250,000 transactions, about 212,600 of them payments. Run them in order.

**Floating point drifts.** The same amounts, summed exactly and as floating point:

```sql playground
SELECT sum(amount) AS exact_total,
       sum(amount::float8) AS floating_point_total
FROM transactions;
```

**Where you round matters.** A 0.5% fee on each payment, rounded per payment, against 0.5% of the total:

```sql playground
SELECT count(*) AS payments,
       sum(round(-amount * 0.005, 2)) AS fees_rounded_per_payment,
       round(sum(-amount) * 0.005, 2) AS fee_on_the_total
FROM transactions
WHERE amount < 0;
```

**Rounding each share.** The fee split 50/30/20, each share rounded on its own:

```sql playground
WITH fees AS (
    SELECT round(-amount * 0.005, 2) AS fee FROM transactions WHERE amount < 0
)
SELECT sum(fee) AS fees_charged,
       sum(round(fee * 0.5, 2) + round(fee * 0.3, 2) + round(fee * 0.2, 2)) AS shares_paid_out,
       count(*) FILTER (WHERE round(fee * 0.5, 2) + round(fee * 0.3, 2) + round(fee * 0.2, 2) <> fee) AS fees_that_dont_add_up,
       count(*) AS fees
FROM fees;
```

**The last share takes the remainder.** It adds up; compare the tax account with its fair share:

```sql playground
WITH fees AS (
    SELECT round(-amount * 0.005, 2) AS fee FROM transactions WHERE amount < 0
),
split AS (
    SELECT fee,
           round(fee * 0.5, 2) AS bank,
           round(fee * 0.3, 2) AS partner,
           fee - round(fee * 0.5, 2) - round(fee * 0.3, 2) AS tax
    FROM fees
)
SELECT sum(fee) AS fees_charged,
       sum(bank + partner + tax) AS shares_paid_out,
       sum(tax) AS tax_received,
       round(sum(fee) * 0.2, 2) AS tax_share_of_the_total
FROM split;
```

**Largest remainder.** In whole cents: each share rounded down, then the leftover cents to the largest remainders. This one takes a few seconds:

```sql playground
WITH fees AS (
    SELECT id, (round(-amount * 0.005, 2) * 100)::bigint AS fee_cents
    FROM transactions WHERE amount < 0
),
shares AS (
    SELECT f.id, f.fee_cents, s.party, s.position,
           floor(f.fee_cents * s.weight)::bigint AS base_cents,
           f.fee_cents * s.weight - floor(f.fee_cents * s.weight) AS remainder
    FROM fees f
    CROSS JOIN (VALUES ('bank', 0.5, 1), ('partner', 0.3, 2), ('tax', 0.2, 3)) AS s(party, weight, position)
),
allocated AS (
    SELECT fee_cents, party,
           base_cents + CASE
               WHEN row_number() OVER (PARTITION BY id ORDER BY remainder DESC, position)
                    <= fee_cents - sum(base_cents) OVER (PARTITION BY id) THEN 1 ELSE 0
           END AS share_cents
    FROM shares
)
SELECT (sum(fee_cents) FILTER (WHERE party = 'bank') / 100.0)::numeric(14, 2) AS fees_charged,
       (sum(share_cents) / 100.0)::numeric(14, 2) AS shares_paid_out,
       (sum(share_cents) FILTER (WHERE party = 'tax') / 100.0)::numeric(14, 2) AS tax_received
FROM allocated;
```

Our results:

| Calculation | Result |
|---|---|
| Exact total vs floating point | 153,351,236.35 vs 153,351,236.3500041 |
| Fees rounded per payment vs fee on the total | 1,354,585.87 vs 1,354,578.47 |
| Shares, each rounded | 1,355,228.37 paid out for 1,354,585.87 charged; 64,250 fees don't add up |
| Last share takes the remainder | Adds up; tax receives 270,277.58 of a fair 270,917.17 |
| Largest remainder | Adds up; tax receives 270,920.08 |

The money type and its allocation are in `samples/src/Bank.Money`, with tests in `samples/tests/Bank.Api.Tests/MoneyTests.cs`: try writing `Allocate` yourself, then run them.

```sh
dotnet test --project samples/tests/Bank.Api.Tests --filter-class "Bank.Api.Tests.MoneyTests"
```

## What breaks next

A customer complains about an overcharged fee. A support engineer, trying to help, fixes it directly: `UPDATE accounts SET balance = balance + 0.40 WHERE id = ...`. The customer is happy. But the account's stored balance no longer matches its transactions, and next month nobody can explain where the R0.40 came from.

Next: [5. A ledger you can audit](./05-a-ledger-you-can-audit).
