-- Seed data, part 1: accounts. Shared by the lab seeder (Seeder.cs) and the in-browser
-- SQL playground on the handbook site, so both get the same shape of data.
-- {accounts} is replaced with a number before this runs. Call setseed() first for
-- repeatable data.
INSERT INTO accounts (holder_name, currency, opened_at)
SELECT
    (ARRAY['Thandi','Sipho','Aisha','Pieter','Lerato','Johan','Naledi','Ravi','Grace','Kabelo'])[1 + floor(random() * 10)::int]
        || ' ' ||
    (ARRAY['Nkosi','Botha','Patel','Dlamini','Smith','Mokoena','Naidoo','van Wyk','Khumalo','Jacobs'])[1 + floor(random() * 10)::int],
    'ZAR',
    now() - interval '6 years' + random() * interval '3 years'
FROM generate_series(1, {accounts});
