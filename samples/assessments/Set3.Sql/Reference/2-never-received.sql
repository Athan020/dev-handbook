-- Worked answer. NOT EXISTS, because NOT IN returns nothing once the subquery contains a NULL.
SELECT a.id AS account_id
FROM accounts a
WHERE NOT EXISTS (
    SELECT 1 FROM transactions t WHERE t.counterparty_account_id = a.id
)
ORDER BY a.id;
