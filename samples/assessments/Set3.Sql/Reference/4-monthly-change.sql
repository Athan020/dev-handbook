-- Worked answer. Filter to 2026 before lag, so a 2025 month is never "the previous month".
SELECT account_id, month, net,
       net - lag(net) OVER (PARTITION BY account_id ORDER BY month) AS change
FROM (
    SELECT account_id, date_trunc('month', created_at)::date AS month, sum(amount) AS net
    FROM transactions
    WHERE created_at >= '2026-01-01' AND created_at < '2027-01-01'
    GROUP BY account_id, date_trunc('month', created_at)
) monthly
ORDER BY account_id, month;
