<script setup lang="ts">
// Two-transaction interleaving simulator for B3 stages 1 and 2.
//
// A small, deterministic model of how PostgreSQL treats two concurrent transactions on one
// account, at each isolation level. The reader steps T1 and T2 in any order.
//   Read committed:   every statement sees what was committed when it started. An UPDATE that
//                     waited for another transaction's row lock re-reads the row afterwards.
//   Repeatable read:  every statement sees the snapshot taken at the transaction's first
//                     statement. Updating a row that another transaction changed and committed
//                     after that snapshot fails with a serialization failure (40001).
//   Serializable:     as repeatable read, plus detection of read/write dependency cycles
//                     between concurrent transactions (write skew), which aborts one (40001).
// It's a teaching model: PostgreSQL may report a failure at a different statement.
import { computed, nextTick, reactive, ref, useId, useTemplateRef, watch } from 'vue'

type Level = 'rc' | 'rr' | 'ser'
type ScenarioKey = 'check-insert' | 'read-write' | 'conditional'
type Status = 'idle' | 'active' | 'blocked' | 'committed' | 'rolledback' | 'aborted'

interface Txn {
  name: string
  delta: number
  pc: number
  status: Status
  snapshot: number | null
  readAt: number | null
  read: number | null
  newBalance: number | null
  pending: number | null // value this transaction will commit for the balance
  inserted: boolean
  committedAt: number | null
  updated: boolean | null
  doomed: boolean
  reason: string
}

const START = 100
const id = useId()

const levels: { key: Level; label: string }[] = [
  { key: 'rc', label: 'Read committed' },
  { key: 'rr', label: 'Repeatable read' },
  { key: 'ser', label: 'Serializable' },
]

const scenarios: { key: ScenarioKey; label: string; summary: string; deltas: [number, number]; steps: (t: number) => string[] }[] = [
  {
    key: 'check-insert',
    label: 'Check, then insert',
    summary: 'Stage 1. Each transfer of 60 sums the account\'s transactions, checks the total, and inserts a debit.',
    deltas: [-60, -60],
    steps: () => ['BEGIN', 'SELECT sum(amount): the balance', 'IF balance < 60: ROLLBACK', 'INSERT a debit of -60', 'COMMIT'],
  },
  {
    key: 'read-write',
    label: 'Read, then write the balance',
    summary: 'Stage 2. T1 withdraws 60 and T2 deposits 50, each by reading the stored balance and writing back a new one.',
    deltas: [-60, 50],
    steps: (t) => ['BEGIN', 'SELECT balance', t === 0 ? 'new balance = balance - 60' : 'new balance = balance + 50', 'UPDATE accounts SET balance = new balance', 'COMMIT'],
  },
  {
    key: 'conditional',
    label: 'Conditional update',
    summary: 'Stage 2\'s fix. Each transfer of 60 changes the balance only if it covers the amount, in one statement.',
    deltas: [-60, -60],
    steps: () => ['BEGIN', 'UPDATE accounts SET balance = balance - 60 WHERE balance >= 60', 'IF no row was updated: ROLLBACK', 'COMMIT'],
  },
]

const level = ref<Level>('rc')
const scenarioKey = ref<ScenarioKey>('check-insert')
const scenario = computed(() => scenarios.find((s) => s.key === scenarioKey.value)!)

const state = reactive({
  clock: 0,
  versions: [{ clock: 0, value: START }] as { clock: number; value: number }[],
  holder: null as number | null, // which transaction holds the account row's lock
  txns: [] as Txn[],
  log: [] as string[],
})

function newTxn(index: number): Txn {
  return {
    name: `T${index + 1}`, delta: scenario.value.deltas[index], pc: 0, status: 'idle',
    snapshot: null, readAt: null, read: null, newBalance: null, pending: null,
    inserted: false, committedAt: null, updated: null, doomed: false, reason: '',
  }
}

function reset() {
  state.clock = 0
  state.versions = [{ clock: 0, value: START }]
  state.holder = null
  state.txns = [newTxn(0), newTxn(1)]
  state.log = []
}
reset()

const latest = () => state.versions[state.versions.length - 1]
const valueAt = (clock: number) => [...state.versions].reverse().find((v) => v.clock <= clock)!.value
const finished = (t: Txn) => t.status === 'committed' || t.status === 'rolledback' || t.status === 'aborted'
const other = (i: number) => state.txns[1 - i]

// The point in time this statement sees: now for read committed, the transaction's snapshot otherwise.
function statementSnapshot(t: Txn) {
  if (level.value === 'rc') return state.clock
  t.snapshot ??= state.clock
  return t.snapshot
}

function end(i: number, status: Status, reason: string) {
  const t = state.txns[i]
  t.status = status
  t.reason = reason
  if (state.holder === i) state.holder = null
  const o = other(i)
  if (o.status === 'blocked') {
    o.status = 'active'
    state.log.push(`${o.name} stops waiting for the lock and carries on.`)
    execute(1 - i)
  }
}

function abort(i: number, why: string) {
  state.log.push(`${state.txns[i].name}: ERROR 40001, could not serialize access ${why}. Rolled back; the client must retry.`)
  end(i, 'aborted', 'Serialization failure (40001): retry')
}

// Taking the row lock for an UPDATE. Returns false if the transaction must wait or was aborted.
function lockRow(i: number): boolean {
  const t = state.txns[i]
  const snap = statementSnapshot(t)
  if (state.holder !== null && state.holder !== i) {
    t.status = 'blocked'
    state.log.push(`${t.name} waits: ${other(i).name} holds the account row's lock.`)
    return false
  }
  if (level.value !== 'rc' && latest().clock > snap) {
    abort(i, 'due to concurrent update')
    return false
  }
  state.holder = i
  return true
}

function execute(i: number) {
  const t = state.txns[i]
  const steps = scenario.value.steps(i)
  if (finished(t) || t.status === 'blocked' || t.pc >= steps.length) return
  state.clock++
  if (t.doomed) {
    abort(i, 'due to read/write dependencies among transactions')
    return
  }

  const key = scenarioKey.value
  const commitStep = steps.length - 1

  if (t.pc === 0) {
    t.status = 'active'
    state.log.push(`${t.name}: BEGIN.`)
  } else if (t.pc === commitStep) {
    commit(i)
    return
  } else if (key === 'check-insert') {
    if (t.pc === 1) {
      const snap = statementSnapshot(t)
      t.readAt = snap
      t.read = valueAt(snap) + (t.inserted ? t.delta : 0)
      state.log.push(`${t.name} reads the balance: ${t.read}.`)
    } else if (t.pc === 2) {
      if (t.read! < 60) {
        state.log.push(`${t.name}: ${t.read} is less than 60, so it rolls back.`)
        end(i, 'rolledback', 'Insufficient funds')
        return
      }
      state.log.push(`${t.name}: ${t.read} covers 60, carry on.`)
    } else if (t.pc === 3) {
      t.inserted = true
      state.log.push(`${t.name} inserts a debit of -60 (not yet committed).`)
    }
  } else if (key === 'read-write') {
    if (t.pc === 1) {
      const snap = statementSnapshot(t)
      t.read = valueAt(snap)
      state.log.push(`${t.name} reads the balance: ${t.read}.`)
    } else if (t.pc === 2) {
      t.newBalance = t.read! + t.delta
      state.log.push(`${t.name} works out the new balance: ${t.newBalance}.`)
    } else if (t.pc === 3) {
      if (!lockRow(i)) return
      t.pending = t.newBalance
      state.log.push(`${t.name} sets the balance to ${t.pending} (not yet committed).`)
    }
  } else {
    if (t.pc === 1) {
      if (!lockRow(i)) return
      // Read committed re-reads the latest committed row after waiting; the others use their snapshot.
      const current = level.value === 'rc' ? latest().value : valueAt(t.snapshot!)
      t.updated = current >= 60
      if (t.updated) {
        t.pending = current - 60
        state.log.push(`${t.name} finds ${current}, which covers 60, and sets the balance to ${t.pending} (not yet committed).`)
      } else {
        state.holder = null
        state.log.push(`${t.name} finds ${current}, which doesn't cover 60: no row updated.`)
      }
    } else if (t.pc === 2) {
      if (!t.updated) {
        state.log.push(`${t.name}: no row was updated, so it rolls back.`)
        end(i, 'rolledback', 'Insufficient funds')
        return
      }
      state.log.push(`${t.name}: one row updated, carry on.`)
    }
  }
  t.pc++
}

function commit(i: number) {
  const t = state.txns[i]
  const o = other(i)
  // Serializable only: a cycle of read/write dependencies. Each transaction read the balance
  // without seeing the other's debit, so no serial order could produce both results.
  if (level.value === 'ser' && scenarioKey.value === 'check-insert' && t.inserted && t.readAt !== null
      && o.inserted && o.readAt !== null && o.status !== 'aborted' && o.status !== 'rolledback') {
    const iMissedTheirs = o.committedAt === null || o.committedAt > t.readAt
    if (iMissedTheirs) {
      if (o.status === 'committed') {
        abort(i, 'due to read/write dependencies among transactions')
        return
      }
      o.doomed = true // the first to commit wins; the other fails at its next statement
    }
  }
  const writes = scenarioKey.value === 'check-insert' ? t.inserted : t.pending !== null
  if (writes) {
    const value = scenarioKey.value === 'check-insert' ? latest().value + t.delta : t.pending!
    state.versions.push({ clock: state.clock, value })
  }
  t.committedAt = state.clock
  state.log.push(`${t.name} commits. The account's balance is now ${latest().value}.`)
  t.pc++
  end(i, 'committed', 'Committed')
}

function choose(nextLevel: Level | null, nextScenario: ScenarioKey | null) {
  if (nextLevel) level.value = nextLevel
  if (nextScenario) scenarioKey.value = nextScenario
  reset()
}

// Run the worst case: alternate T1 and T2, stepping the other when one can't move.
function interleave() {
  reset()
  for (let n = 0, turn = 0; n < 40 && !state.txns.every(finished); n++, turn = 1 - turn) {
    const t = state.txns[turn]
    execute(finished(t) || t.status === 'blocked' ? 1 - turn : turn)
  }
}

function oneAfterTheOther() {
  reset()
  for (const i of [0, 1]) {
    for (let n = 0; n < 10 && !finished(state.txns[i]); n++) execute(i)
  }
}

const done = computed(() => state.txns.every(finished))
const committed = computed(() => state.txns.filter((t) => t.status === 'committed'))
const expected = computed(() => START + committed.value.reduce((sum, t) => sum + t.delta, 0))
const verdict = computed(() => {
  if (!done.value) return null
  const final = latest().value
  if (final < 0) return { ok: false, text: `Overdrawn: the balance is ${final}. Both transfers passed the check against the same balance.` }
  if (final !== expected.value) {
    const lost = committed.value.find((t) => t.pending !== null && t.pending !== final) ?? committed.value[0]
    return { ok: false, text: `Lost update: the balance is ${final}, but the committed changes add up to ${expected.value}. ${lost.name}'s change vanished.` }
  }
  const retry = state.txns.find((t) => t.status === 'aborted')
  return {
    ok: true,
    text: `Correct: the balance is ${final}.` + (retry ? ` ${retry.name} failed with a serialization failure and must be retried; run again, it would see the new balance.` : ''),
  }
})

// Keep the newest line of the log in view.
const logList = useTemplateRef<HTMLOListElement>('logList')
watch(() => state.log.length, async () => {
  await nextTick()
  if (logList.value) logList.value.scrollTop = logList.value.scrollHeight
})

const statusLabel: Record<Status, string> = {
  idle: 'Not started', active: 'Running', blocked: 'Waiting for a lock', committed: 'Committed', rolledback: 'Rolled back', aborted: 'Aborted',
}
</script>

<template>
  <div class="simulator interleaving">
    <div class="header">
      <span class="title">Two transactions, one account</span>
      <span class="note">The account starts with {{ START }}. Step each transaction yourself, or run a preset.</span>
    </div>

    <fieldset class="choices">
      <legend>Scenario</legend>
      <button
        v-for="s in scenarios" :key="s.key" type="button"
        :aria-pressed="scenarioKey === s.key" :data-scenario="s.key"
        @click="choose(null, s.key)"
      >{{ s.label }}</button>
    </fieldset>
    <fieldset class="choices">
      <legend>Isolation level</legend>
      <button
        v-for="l in levels" :key="l.key" type="button"
        :aria-pressed="level === l.key" :data-level="l.key"
        @click="choose(l.key, null)"
      >{{ l.label }}</button>
    </fieldset>
    <p class="summary">{{ scenario.summary }}</p>

    <div class="txns">
      <section v-for="(t, i) in state.txns" :key="t.name" class="txn" :data-txn="t.name" :data-status="t.status">
        <h4>
          {{ t.name }}
          <span class="badge" :class="t.status">{{ statusLabel[t.status] }}</span>
        </h4>
        <ol>
          <li
            v-for="(step, s) in scenario.steps(i)" :key="s"
            :class="{ done: s < t.pc, current: s === t.pc && !['committed', 'rolledback', 'aborted'].includes(t.status) }"
          ><code>{{ step }}</code></li>
        </ol>
        <p v-if="t.reason && t.status !== 'committed'" class="reason">{{ t.reason }}</p>
        <button
          type="button" class="step" :aria-controls="`${id}-log`"
          :disabled="['committed', 'rolledback', 'aborted', 'blocked'].includes(t.status)"
          @click="execute(i)"
        >Step {{ t.name }}</button>
      </section>
    </div>

    <div class="actions">
      <button type="button" data-action="interleave" @click="interleave">Run interleaved</button>
      <button type="button" data-action="serial" @click="oneAfterTheOther">Run one after the other</button>
      <button type="button" data-action="reset" @click="reset">Reset</button>
      <span class="balance">Committed balance: <strong data-stat="balance">{{ state.versions[state.versions.length - 1].value }}</strong></span>
    </div>

    <ol :id="`${id}-log`" ref="logList" class="log" aria-live="polite">
      <li v-for="(line, n) in state.log" :key="n">{{ line }}</li>
    </ol>

    <p v-if="verdict" class="verdict" :class="{ bad: !verdict.ok }" data-stat="verdict" role="status">{{ verdict.text }}</p>
  </div>
</template>

<style scoped>
.simulator {
  margin: 16px 0;
  padding: 12px 16px 16px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background-color: var(--vp-c-bg-soft);
  font-size: 14px;
}

.header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 4px 12px;
  margin-bottom: 8px;
}

.title {
  font-weight: 600;
}

.note,
.summary {
  color: var(--vp-c-text-2);
  font-size: 13px;
}

.summary {
  margin: 8px 0;
}

.choices {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 8px 0 0;
  padding: 0;
  border: 0;
}

.choices legend {
  float: left;
  min-width: 120px;
  padding: 4px 0;
  font-weight: 500;
}

button {
  padding: 4px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 16px;
  background-color: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font-size: 13px;
  cursor: pointer;
}

button[aria-pressed='true'] {
  border-color: var(--vp-c-brand-1);
  background-color: var(--vp-c-brand-soft);
  color: var(--vp-c-brand-1);
}

button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

button:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}

.txns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-top: 8px;
}

@media (max-width: 640px) {
  .txns {
    grid-template-columns: 1fr;
  }
}

.txn {
  padding: 8px 12px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-c-bg);
}

.txn h4 {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 0 0 6px;
  font-size: 14px;
}

.badge {
  padding: 1px 8px;
  border-radius: 10px;
  background-color: var(--vp-c-default-soft);
  font-size: 12px;
  font-weight: 500;
}

.badge.committed {
  background-color: var(--vp-c-green-soft);
  color: var(--vp-c-green-1);
}

.badge.blocked {
  background-color: var(--vp-c-yellow-soft);
  color: var(--vp-c-yellow-1);
}

.badge.aborted,
.badge.rolledback {
  background-color: var(--vp-c-red-soft);
  color: var(--vp-c-red-1);
}

.txn ol {
  margin: 0 0 8px;
  padding-left: 20px;
}

.txn li {
  margin: 2px 0;
  color: var(--vp-c-text-3);
}

.txn li code {
  font-size: 12px;
  white-space: normal;
}

.txn li.done {
  color: var(--vp-c-text-1);
}

.txn li.current {
  color: var(--vp-c-text-1);
  font-weight: 600;
}

.txn li.current::marker {
  color: var(--vp-c-brand-1);
}

.reason {
  margin: 0 0 8px;
  font-size: 13px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-top: 12px;
}

.balance {
  margin-left: auto;
  font-variant-numeric: tabular-nums;
}

.log {
  max-height: 220px;
  margin: 12px 0 0;
  padding: 8px 12px 8px 32px;
  overflow-y: auto;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-c-bg);
  font-size: 13px;
}

.log:empty {
  display: none;
}

.verdict {
  margin: 12px 0 0;
  padding: 8px 12px;
  border-left: 4px solid var(--vp-c-green-1);
  background-color: var(--vp-c-green-soft);
}

.verdict.bad {
  border-left-color: var(--vp-c-red-1);
  background-color: var(--vp-c-red-soft);
}
</style>
