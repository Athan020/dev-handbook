-- The data the questions are scored against. Don't read it before you've finished the set.
INSERT INTO branches (id, name) VALUES
    (1, 'Durban'), (2, 'Cape Town'), (3, 'Pretoria'), (4, 'Gqeberha'), (5, 'Bloemfontein'), (6, 'Polokwane');

INSERT INTO accounts (id, branch_id, holder_name, opened_on, closed_on) VALUES
    (1, 1, 'Thandi Nkosi',   '2025-01-10', NULL),
    (2, 1, 'Johan Botha',    '2025-02-01', NULL),
    (3, 2, 'Aisha Patel',    '2025-03-15', NULL),
    (4, 2, 'Pieter Smit',    '2024-11-20', '2026-01-31'),
    (5, 3, 'Lerato Mokoena', '2024-06-01', NULL),
    (6, 5, 'Sipho Dlamini',  '2023-04-01', '2025-06-30'),
    (7, 6, 'Naledi Khumalo', '2025-09-09', NULL),
    (8, 3, 'Ruan van Wyk',   '2026-04-01', NULL);

INSERT INTO transactions (id, account_id, counterparty_account_id, amount, created_at) VALUES
    (1,  1, NULL,   -300.00, '2026-03-05 10:00:00'),
    (2,  1, 2,     -1000.00, '2026-03-20 09:00:00'),
    (3,  2, 1,      1000.00, '2026-03-20 09:00:00'),
    (4,  1, NULL,  -1000.00, '2026-03-02 08:00:00'),
    (5,  3, NULL,   2500.00, '2025-12-15 10:00:00'),
    (6,  3, NULL,    -40.00, '2026-01-04 11:00:00'),
    (7,  3, 5,       -60.00, '2026-04-10 13:00:00'),
    (8,  5, 3,        60.00, '2026-04-10 13:00:00'),
    (9,  5, NULL,    100.00, '2026-01-15 09:00:00'),
    (10, 7, NULL,    -25.50, '2026-02-28 23:59:59'),
    (11, 7, NULL,    -25.50, '2026-03-01 00:00:00'),
    (12, 2, NULL,     -0.01, '2026-05-01 00:00:00'),
    (13, 4, NULL,     10.00, '2025-11-01 09:00:00');
