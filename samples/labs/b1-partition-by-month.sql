-- B1 stage 7: a copy of the transactions table, partitioned by month.
-- Run it with:
--   docker compose exec -T postgres psql -U bank -d bank < labs/b1-partition-by-month.sql
-- It leaves the original table alone. Drop the copy with: DROP TABLE transactions_by_month;
\timing on

DROP TABLE IF EXISTS transactions_by_month;

CREATE TABLE transactions_by_month (LIKE transactions INCLUDING DEFAULTS)
    PARTITION BY RANGE (created_at);

-- A primary key on a partitioned table must include the partition key.
ALTER TABLE transactions_by_month ADD PRIMARY KEY (id, created_at);

-- One partition per month that has data. New months need their partition created in
-- advance (by a scheduled job, or an extension such as pg_partman), or inserts fail.
DO $$
DECLARE
    month date;
BEGIN
    FOR month IN
        SELECT generate_series(date_trunc('month', min(created_at)), date_trunc('month', max(created_at)), interval '1 month')::date
        FROM transactions
    LOOP
        EXECUTE format(
            'CREATE TABLE %I PARTITION OF transactions_by_month FOR VALUES FROM (%L) TO (%L)',
            'transactions_' || to_char(month, 'YYYY_MM'), month, month + interval '1 month');
    END LOOP;
END $$;

INSERT INTO transactions_by_month SELECT * FROM transactions;

-- Created on the parent, the index is created on every partition.
CREATE INDEX ON transactions_by_month (account_id, created_at DESC, id DESC);
ANALYZE transactions_by_month;

SELECT count(*) AS partitions FROM pg_inherits WHERE inhparent = 'transactions_by_month'::regclass;
