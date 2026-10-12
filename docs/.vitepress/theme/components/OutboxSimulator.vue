<script setup lang="ts">
// Dual-write and outbox simulator for B2 stage 4.
//
// 1,000 transfers, each of which should be saved in the database and announced with an event
// that a consumer acts on exactly once. Any process can crash between two steps, with the
// chosen probability. The strategies:
//   afterCommit:  commit the transfer, then publish            (a crash in between loses the event)
//   beforeCommit: publish, then commit the transfer            (a crash in between leaves a ghost event)
//   outbox:       commit transfer + outbox row together; a relay publishes, then marks it sent
//                 (a relay crash in between republishes), consumer not idempotent
//   outboxIdempotent: the same, with an idempotent consumer (stage 3)
// The same seeded random numbers drive every strategy.
import { computed, ref, useId } from 'vue'

type Strategy = 'afterCommit' | 'beforeCommit' | 'outbox' | 'outboxIdempotent'
const TRANSFERS = 1000

const strategies: { key: Strategy; label: string; steps: string }[] = [
  { key: 'afterCommit', label: 'Commit, then publish', steps: 'commit the transfer, then publish the event' },
  { key: 'beforeCommit', label: 'Publish, then commit', steps: 'publish the event, then commit the transfer' },
  { key: 'outbox', label: 'Outbox, plain consumer', steps: 'commit the transfer and an outbox row together; a relay publishes, then marks the row sent' },
  { key: 'outboxIdempotent', label: 'Outbox, idempotent consumer', steps: 'as above, and the consumer ignores events it has already handled' },
]

const id = useId()
const crashPercent = ref(10)
const selected = ref<Strategy>('afterCommit')

function seeded(seed: number) {
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function simulate(strategy: Strategy, crashChance: number) {
  const rng = seeded(11)
  const crashes = () => rng() < crashChance
  let correct = 0
  let lost = 0
  let ghost = 0
  let duplicated = 0
  let published = 0

  for (let t = 0; t < TRANSFERS; t++) {
    let saved = false
    let effects = 0
    if (strategy === 'afterCommit') {
      saved = true
      if (!crashes()) {
        published++
        effects = 1
      }
    } else if (strategy === 'beforeCommit') {
      published++
      effects = 1
      if (!crashes()) saved = true
    } else {
      saved = true
      let marked = false
      while (!marked) {
        published++
        if (strategy === 'outbox' || effects === 0) effects++
        if (!crashes()) marked = true
      }
    }
    if (saved && effects === 1) correct++
    else if (saved && effects === 0) lost++
    else if (!saved && effects > 0) ghost++
    else if (effects > 1) duplicated++
  }
  return { correct, lost, ghost, duplicated, published }
}

const results = computed(() => strategies.map((s) => ({ ...s, ...simulate(s.key, crashPercent.value / 100) })))
const current = computed(() => results.value.find((r) => r.key === selected.value)!)
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Dual write and outbox simulator</span>
      <span class="note">{{ TRANSFERS.toLocaleString('en-GB') }} transfers</span>
    </div>

    <div class="controls">
      <label :for="`${id}-crash`">Chance of crashing between two steps <output>{{ crashPercent }}%</output></label>
      <input :id="`${id}-crash`" v-model.number="crashPercent" type="range" min="0" max="30" step="1" />
    </div>

    <div class="table-wrap">
      <table>
        <caption>Each transfer should be saved, with exactly one effect downstream</caption>
        <thead>
          <tr>
            <th scope="col">Strategy</th>
            <th scope="col">Correct</th>
            <th scope="col">Event lost</th>
            <th scope="col">Ghost event</th>
            <th scope="col">Duplicated</th>
            <th scope="col">Events published</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in results" :key="r.key" :class="{ selected: r.key === selected }" :data-strategy="r.key" @click="selected = r.key">
            <th scope="row">
              <label class="pick">
                <input v-model="selected" type="radio" :name="`${id}-strategy`" :value="r.key" />
                {{ r.label }}
              </label>
            </th>
            <td data-col="correct">{{ r.correct }}</td>
            <td data-col="lost" :class="{ bad: r.lost > 0 }">{{ r.lost }}</td>
            <td data-col="ghost" :class="{ bad: r.ghost > 0 }">{{ r.ghost }}</td>
            <td data-col="duplicated" :class="{ bad: r.duplicated > 0 }">{{ r.duplicated }}</td>
            <td>{{ r.published.toLocaleString('en-GB') }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <p class="explain">
      <strong>{{ current.label }}:</strong> {{ current.steps }}.
      <template v-if="current.key === 'afterCommit'">A crash after the commit means the money moved and nobody downstream ever hears about it.</template>
      <template v-else-if="current.key === 'beforeCommit'">A crash after publishing means consumers act on a transfer that never happened: a ghost.</template>
      <template v-else-if="current.key === 'outbox'">Nothing is lost, but a relay crash between publishing and marking publishes the event again, and a plain consumer acts twice.</template>
      <template v-else>Nothing lost, no ghosts, and repeats are ignored: every transfer has exactly one effect.</template>
    </p>
  </div>
</template>

<style scoped>
.simulator {
  margin: 16px 0;
  padding: 12px 16px 16px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background-color: var(--vp-c-bg-soft);
}

.header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 4px 12px;
  margin-bottom: 8px;
}

.title {
  font-size: 14px;
  font-weight: 600;
}

.note {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.controls {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr);
  gap: 6px 16px;
  align-items: center;
  font-size: 14px;
}

.controls label {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.controls output {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.controls input {
  width: 100%;
  accent-color: var(--vp-c-brand-1);
}

@media (max-width: 520px) {
  .controls {
    grid-template-columns: 1fr;
  }
}

.table-wrap {
  margin-top: 12px;
  overflow-x: auto;
}

.table-wrap table {
  display: table;
  width: 100%;
  margin: 0;
  border-collapse: collapse;
  font-size: 14px;
}

caption {
  margin-bottom: 6px;
  font-size: 13px;
  color: var(--vp-c-text-2);
  text-align: left;
}

th,
td {
  padding: 6px 10px;
  border: 1px solid var(--vp-c-divider);
  text-align: right;
  font-variant-numeric: tabular-nums;
}

th[scope='row'],
thead th:first-child {
  text-align: left;
  font-weight: 500;
}

tr.selected {
  background-color: var(--vp-c-brand-soft);
}

tbody tr {
  cursor: pointer;
}

td.bad {
  color: var(--vp-c-danger-1);
  font-weight: 600;
}

.pick {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.pick input {
  accent-color: var(--vp-c-brand-1);
}

.explain {
  margin: 12px 0 0;
  font-size: 14px;
}

input:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
