CREATE TABLE branches (
    id integer PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE accounts (
    id integer PRIMARY KEY,
    branch_id integer NOT NULL REFERENCES branches (id),
    holder_name text NOT NULL,
    opened_on date NOT NULL,
    closed_on date            -- NULL while the account is open
);

CREATE TABLE transactions (
    id integer PRIMARY KEY,
    account_id integer NOT NULL REFERENCES accounts (id),
    counterparty_account_id integer REFERENCES accounts (id), -- NULL when there's no other account
    amount numeric(12, 2) NOT NULL,                           -- negative for money leaving the account
    created_at timestamp NOT NULL
);
