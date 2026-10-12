# Transfer fees: practice task

Time: 60 minutes. This is a practice project task: `Problems/` is the codebase you're working on, and it contains the reported bugs on purpose. Don't open `Reference/` until you've finished.

The bank charges a fee on every transfer. `FeeCalculator.Default` holds the current tariff:

- every transfer pays 0.5% of its amount, with a minimum fee of 1.00;
- a transfer between two different currencies pays an extra 1% of its amount;
- the total fee is rounded to the nearest cent.

## Your task

1. **Fix a rounding bug.** A customer writes: "A transfer of 533.00 was charged 2.66. Your tariff says 0.5%, rounded to the nearest cent with halves rounded up, so it should be 2.67."
2. **Fix a currency bug.** A customer writes: "I moved money from my ZAR account to my other ZAR account and was charged the cross-currency fee." The second account's currency was stored as `zar`. Currency codes are the same currency whatever their case.
3. **Add a premium cap.** Premium customers' total fee, after rounding, is capped at 25.00. Standard customers have no cap.

Make the smallest changes that do this, in the style of the existing code. Don't change the tests.

## Running the tests

From `samples/assessments`:

```sh
dotnet test --project Set4.Project --filter-trait "Tests=Visible"   # the tests you'd see in the repository
dotnet test --project Set4.Project                                  # everything, including the hidden tests
```
