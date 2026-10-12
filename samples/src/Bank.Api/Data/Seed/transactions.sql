-- Seed data, part 2: transactions with ids {start} to {end} of {transactions}.
-- Shared by the lab seeder (Seeder.cs) and the in-browser SQL playground.
--
-- Account ids are drawn from random()^3, so low ids get far more transactions. With
-- 100,000 accounts, account 1 gets about 2% of all rows and the busiest 1% of accounts
-- get about 20%. created_at rises with the row number, as it does in an append-only
-- table. Each row is one kind: salary (5%), incoming transfer (10%), outgoing transfer
-- (25%) or spending (60%); only transfers have a counterparty.
INSERT INTO transactions (account_id, counterparty_account_id, amount, description, created_at)
SELECT
    account_id,
    CASE WHEN kind IN ('in', 'out') THEN 1 + floor(r_other * {accounts})::int END,
    CASE kind
        WHEN 'salary' THEN round((8000 + r_amount * 42000)::numeric, 2)
        WHEN 'in' THEN round((50 + r_amount * 4950)::numeric, 2)
        WHEN 'out' THEN -round((50 + r_amount * 4950)::numeric, 2)
        ELSE -round((5 + r_amount * 1495)::numeric, 2)
    END,
    CASE kind
        WHEN 'salary' THEN 'Salary'
        WHEN 'in' THEN 'Incoming transfer'
        WHEN 'out' THEN 'Transfer'
        ELSE (ARRAY['Card payment','Debit order','ATM withdrawal','Airtime','Groceries','Fuel','Online purchase','Bank fee'])[1 + floor(r_other * 8)::int]
    END,
    created_at
FROM (
    SELECT
        account_id,
        CASE WHEN k < 0.05 THEN 'salary' WHEN k < 0.15 THEN 'in' WHEN k < 0.40 THEN 'out' ELSE 'spend' END AS kind,
        r_amount, r_other, created_at
    FROM (
        -- random() is volatile, so Postgres evaluates it per row and keeps this subquery separate.
        SELECT
            1 + floor({accounts} * power(random(), 3))::int AS account_id,
            random() AS k,
            random() AS r_amount,
            random() AS r_other,
            now() - interval '2 years' + (n::double precision / {transactions}) * interval '2 years' + random() * interval '1 hour' AS created_at
        FROM generate_series({start}, {end}) AS n
    ) AS raw
) AS generated;
