-- The sample data shown with the questions.
INSERT INTO branches (id, name) VALUES
    (1, 'Durban'), (2, 'Cape Town'), (3, 'Pretoria');

INSERT INTO accounts (id, branch_id, holder_name, opened_on, closed_on) VALUES
    (1, 1, 'Thandi Nkosi',   '2025-01-10', NULL),
    (2, 1, 'Johan Botha',    '2025-02-01', NULL),
    (3, 2, 'Aisha Patel',    '2025-03-15', NULL),
    (4, 2, 'Pieter Smit',    '2024-11-20', '2026-01-31'),
    (5, 3, 'Lerato Mokoena', '2024-06-01', '2025-12-31');

INSERT INTO transactions (id, account_id, counterparty_account_id, amount, created_at) VALUES
    (1, 1, 2, -500.00, '2026-01-10 10:00:00'),
    (2, 2, 1,  500.00, '2026-01-10 10:00:00'),
    (3, 1, 3, -200.00, '2026-01-20 09:30:00'),
    (4, 3, 1,  200.00, '2026-01-20 09:30:00'),
    (5, 3, 2,  -50.00, '2026-02-03 12:00:00'),
    (6, 2, 3,   50.00, '2026-02-03 12:00:00'),
    (7, 1, 2, -500.00, '2026-02-15 08:00:00'),
    (8, 2, 1,  500.00, '2026-02-15 08:00:00');
