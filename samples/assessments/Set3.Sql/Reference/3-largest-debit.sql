-- Worked answer. row_number, not rank, so ties produce one row; ordering by created_at picks
-- the earliest of equal debits.
SELECT account_id, amount, created_at
FROM (
    SELECT account_id, amount, created_at,
           row_number() OVER (PARTITION BY account_id ORDER BY amount, created_at) AS position
    FROM transactions
    WHERE amount < 0
) debits
WHERE position = 1
ORDER BY account_id;
