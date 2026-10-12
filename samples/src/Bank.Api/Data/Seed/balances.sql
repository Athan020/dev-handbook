-- B3 stage 2: set each account's stored balance to the sum of its transactions.
UPDATE accounts a
SET balance = t.total
FROM (SELECT account_id, sum(amount) AS total FROM transactions GROUP BY account_id) t
WHERE a.id = t.account_id;
