-- Worked answer. The open-account condition goes in the ON clause, so branches with no open
-- accounts survive the left join, and count(a.id) counts them as 0.
SELECT b.name AS branch_name, count(a.id) AS open_accounts
FROM branches b
LEFT JOIN accounts a ON a.branch_id = b.id AND a.closed_on IS NULL
GROUP BY b.id, b.name
ORDER BY open_accounts DESC, branch_name;
