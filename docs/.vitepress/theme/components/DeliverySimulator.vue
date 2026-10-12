<script setup lang="ts">
// Duplicate delivery simulator for B2 stage 3.
//
// 1,000 messages, each delivered until a consumer acknowledges it. Handling a message takes
// one or two steps, and the consumer can crash after any step with the chosen probability.
// A crash means no acknowledgement, so the broker delivers the message again. Each strategy
// differs only in what the consumer records, and when:
//   none:        do the side effect                                  -> acknowledge
//   markFirst:   record "processed", then do the side effect          -> acknowledge
//   effectFirst: do the side effect, then record "processed"          -> acknowledge
//   together:    do the side effect and record it in one transaction  -> acknowledge
// The same seeded random numbers drive every strategy, so the comparison is fair.
import { computed, ref, useId } from 'vue'

type Strategy = 'none' | 'markFirst' | 'effectFirst' | 'together'
const MESSAGES = 1000

const strategies: { key: Strategy; label: string; steps: string }[] = [
  { key: 'none', label: 'No deduplication', steps: 'side effect, then acknowledge' },
  { key: 'markFirst', label: 'Record first, then act', steps: 'record "processed", side effect, acknowledge' },
  { key: 'effectFirst', label: 'Act first, then record', steps: 'side effect, record "processed", acknowledge' },
  { key: 'together', label: 'Act and record in one transaction', steps: 'side effect + record (atomic), acknowledge' },
]

const id = useId()
const crashPercent = ref(10)
const selected = ref<Strategy>('effectFirst')

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
  const rng = seeded(7)
  const crashes = () => rng() < crashChance
  let deliveries = 0
  let exactlyOnce = 0
  let duplicated = 0
  let lost = 0

  for (let m = 0; m < MESSAGES; m++) {
    let effects = 0
    let recorded = false
    let acknowledged = false
    while (!acknowledged) {
      deliveries++
      if (strategy === 'none') {
        effects++
        if (crashes()) continue
      } else if (strategy === 'markFirst') {
        if (!recorded) {
          recorded = true
          if (crashes()) continue
          effects++
          if (crashes()) continue
        }
      } else if (strategy === 'effectFirst') {
        if (!recorded) {
          effects++
          if (crashes()) continue
          recorded = true
          if (crashes()) continue
        }
      } else {
        // The side effect and the record commit together, or neither does.
        if (!recorded) {
          if (crashes()) continue // crashed before committing: nothing happened
          effects++
          recorded = true
          if (crashes()) continue // crashed after committing: the redelivery will be skipped
        }
      }
      acknowledged = true
    }
    if (effects === 1) exactlyOnce++
    else if (effects > 1) duplicated++
    else lost++
  }
  return { deliveries, exactlyOnce, duplicated, lost }
}

const results = computed(() =>
  strategies.map((s) => ({ ...s, ...simulate(s.key, crashPercent.value / 100) })),
)
const current = computed(() => results.value.find((r) => r.key === selected.value)!)
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Duplicate delivery simulator</span>
      <span class="note">{{ MESSAGES.toLocaleString('en-GB') }} messages</span>
    </div>

    <div class="controls">
      <label :for="`${id}-crash`">Chance of crashing after each step <output>{{ crashPercent }}%</output></label>
      <input :id="`${id}-crash`" v-model.number="crashPercent" type="range" min="0" max="30" step="1" />
    </div>

    <div class="table-wrap">
      <table>
        <caption>What happened to each message's side effect, after every message was eventually acknowledged</caption>
        <thead>
          <tr>
            <th scope="col">Strategy</th>
            <th scope="col">Exactly once</th>
            <th scope="col">Duplicated</th>
            <th scope="col">Lost</th>
            <th scope="col">Deliveries</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="r in results"
            :key="r.key"
            :class="{ selected: r.key === selected }"
            :data-strategy="r.key"
            @click="selected = r.key"
          >
            <th scope="row">
              <label class="pick">
                <input v-model="selected" type="radio" :name="`${id}-strategy`" :value="r.key" />
                {{ r.label }}
              </label>
            </th>
            <td data-col="once">{{ r.exactlyOnce }}</td>
            <td data-col="duplicated" :class="{ bad: r.duplicated > 0 }">{{ r.duplicated }}</td>
            <td data-col="lost" :class="{ bad: r.lost > 0 }">{{ r.lost }}</td>
            <td>{{ r.deliveries.toLocaleString('en-GB') }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <p class="explain">
      <strong>{{ current.label }}:</strong> {{ current.steps }}.
      <template v-if="current.key === 'none'">Every crash after the side effect means it happens again on redelivery.</template>
      <template v-else-if="current.key === 'markFirst'">A crash between recording and acting leaves the message marked as done when it wasn't: the redelivery skips it, and the side effect never happens.</template>
      <template v-else-if="current.key === 'effectFirst'">A crash between acting and recording means the redelivery doesn't know it was done, and does it again. Fewer duplicates, not none.</template>
      <template v-else>The record and the side effect commit together, so a redelivery either finds both or neither.</template>
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

/* Undo .vp-doc table styles that don't suit a compact results table. */
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
