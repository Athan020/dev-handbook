// One in-browser Postgres (PGlite) per page, shared by every SQL playground on it.
// Sharing is deliberate: a reader who creates an index in one playground should see the
// next playground's query use it. Moving to another page starts a fresh database.
//
// The schema and seed scripts are the same files the lab seeder uses (samples/), scaled
// down so the database starts in a few seconds.
import type { PGlite, Results } from '@electric-sql/pglite'
import schemaSql from '../../../../samples/src/Bank.Api/Data/Seed/schema.sql?raw'
import accountsSql from '../../../../samples/src/Bank.Api/Data/Seed/accounts.sql?raw'
import transactionsSql from '../../../../samples/src/Bank.Api/Data/Seed/transactions.sql?raw'
import balancesSql from '../../../../samples/src/Bank.Api/Data/Seed/balances.sql?raw'

export const DATASET = { accounts: 5_000, transactions: 250_000 } as const

let current: { page: string; db: Promise<PGlite> } | undefined
// PGlite runs one statement at a time; queue runs so playgrounds don't interleave.
let queue: Promise<unknown> = Promise.resolve()

function fill(sql: string, values: Record<string, number>): string {
  return sql.replace(/\{(\w+)\}/g, (match, key: string) => (key in values ? String(values[key]) : match))
}

async function create(): Promise<PGlite> {
  const { PGlite } = await import('@electric-sql/pglite')
  const db = new PGlite()
  const values = { ...DATASET, start: 1, end: DATASET.transactions }
  await db.exec(schemaSql)
  // Same seed as the lab, so the data has the same shape. Foreign key checks are skipped
  // while loading generated rows, which are valid by construction.
  await db.exec('SELECT setseed(0.42); SET session_replication_role = replica;')
  await db.exec(fill(accountsSql, values))
  await db.exec(fill(transactionsSql, values))
  await db.exec(balancesSql)
  await db.exec('RESET session_replication_role; ANALYZE;')
  return db
}

/** The database for this page, created on first use. */
export function database(page: string): Promise<PGlite> {
  if (current?.page !== page) {
    const previous = current
    current = { page, db: create() }
    previous?.db.then((db) => db.close()).catch(() => {})
  }
  return current.db
}

/** Throw away this page's database; the next run starts from fresh data. */
export function resetDatabase(page: string): void {
  if (current?.page === page) {
    const previous = current
    current = undefined
    previous.db.then((db) => db.close()).catch(() => {})
  }
}

/** Run one or more statements, in order, after any run already in progress. */
export function run(page: string, sql: string): Promise<Results<unknown[]>[]> {
  const next = queue.then(async () => (await database(page)).exec(sql, { rowMode: 'array' }))
  queue = next.catch(() => {})
  return next
}
