<script setup lang="ts">
// Rate limiter visualiser for B4 stage 6 and the capstone.
//
// One pattern of requests, sent to three limiters with the same limit, side by side:
//   Fixed window:   at most L requests in each whole second (0-1 s, 1-2 s, ...).
//   Sliding window: at most L requests in any one-second span (an exact log of accepted times).
//   Token bucket:   a bucket of up to B tokens, refilled at L a second; each request takes one.
// Each row shows, per 100 ms, how many requests were accepted and refused.
import { computed, reactive, useId } from 'vue'

type Scenario = 'steady' | 'burst' | 'boundary' | 'quiet'
type Algorithm = 'fixed' | 'sliding' | 'bucket'

const SECONDS = 4
const SLOT = 0.1

const scenarios: { key: Scenario; label: string; describe: string }[] = [
  { key: 'steady', label: 'Steady, over the limit', describe: '15 requests a second, evenly spread.' },
  { key: 'burst', label: 'One big burst', describe: '30 requests at once, 1 second in, then nothing.' },
  { key: 'boundary', label: 'Bursts at a window edge', describe: '10 requests just before 1 second, and 10 just after.' },
  { key: 'quiet', label: 'Quiet, then busy', describe: '2 a second for 2 seconds, then 20 at once, then 15 a second.' },
]

const algorithms: { key: Algorithm; label: string }[] = [
  { key: 'fixed', label: 'Fixed window' },
  { key: 'sliding', label: 'Sliding window' },
  { key: 'bucket', label: 'Token bucket' },
]

const id = useId()
const s = reactive({ scenario: 'boundary' as Scenario, limit: 10, burst: 10 })

function arrivals(scenario: Scenario): number[] {
  const times: number[] = []
  const even = (from: number, to: number, perSecond: number) => {
    for (let t = from; t < to - 1e-9; t += 1 / perSecond) times.push(+t.toFixed(4))
  }
  if (scenario === 'steady') even(0, SECONDS, 15)
  if (scenario === 'burst') for (let i = 0; i < 30; i++) times.push(1)
  if (scenario === 'boundary') {
    for (let i = 0; i < 10; i++) times.push(0.95 + i * 0.004)
    for (let i = 0; i < 10; i++) times.push(1.01 + i * 0.004)
  }
  if (scenario === 'quiet') {
    even(0, 2, 2)
    for (let i = 0; i < 20; i++) times.push(2)
    even(2.1, SECONDS, 15)
  }
  return times.sort((a, b) => a - b)
}

function decide(algorithm: Algorithm, times: number[], limit: number, burst: number): boolean[] {
  const accepted: number[] = []
  let tokens = burst
  let last = 0
  return times.map((t) => {
    let ok: boolean
    if (algorithm === 'fixed') {
      const window = Math.floor(t + 1e-9)
      ok = accepted.filter((a) => Math.floor(a + 1e-9) === window).length < limit
    } else if (algorithm === 'sliding') {
      ok = accepted.filter((a) => a > t - 1).length < limit
    } else {
      tokens = Math.min(burst, tokens + (t - last) * limit)
      last = t
      ok = tokens >= 1
      if (ok) tokens -= 1
    }
    if (ok) accepted.push(t)
    return ok
  })
}

// The most accepted requests in any one-second span: what the protected service really saw.
function busiestSecond(times: number[], decisions: boolean[]) {
  const accepted = times.filter((_, i) => decisions[i])
  let best = 0
  for (let i = 0, j = 0; j < accepted.length; j++) {
    while (accepted[j] - accepted[i] >= 1) i++
    best = Math.max(best, j - i + 1)
  }
  return best
}

const times = computed(() => arrivals(s.scenario))
const rows = computed(() =>
  algorithms.map((a) => {
    const decisions = decide(a.key, times.value, s.limit, s.burst)
    const slots = Array.from({ length: SECONDS / SLOT }, () => ({ accepted: 0, refused: 0 }))
    times.value.forEach((t, i) => {
      const slot = slots[Math.min(slots.length - 1, Math.floor(t / SLOT + 1e-9))]
      if (decisions[i]) slot.accepted++
      else slot.refused++
    })
    return {
      ...a,
      slots,
      accepted: decisions.filter(Boolean).length,
      refused: decisions.filter((d) => !d).length,
      busiest: busiestSecond(times.value, decisions),
    }
  }),
)

// Chart geometry: one strip per algorithm, stacked bars per 100 ms. Labels are HTML, so they
// stay readable when the chart is narrow.
const W = 600
const ROW_H = 56
const maxSlot = computed(() => Math.max(1, ...rows.value.flatMap((r) => r.slots.map((x) => x.accepted + x.refused))))
const slotW = W / (SECONDS / SLOT)
const barW = slotW - 2
const x = (slot: number) => slot * slotW + 1
const h = (n: number) => (n / maxSlot.value) * (ROW_H - 6)

const current = computed(() => scenarios.find((x) => x.key === s.scenario)!)
</script>

<template>
  <div class="simulator rate-limiter">
    <div class="header">
      <span class="title">Rate limiter visualiser</span>
      <span class="note">The same requests, three limiters, the same limit</span>
    </div>

    <fieldset class="choices">
      <legend>Requests</legend>
      <button
        v-for="sc in scenarios" :key="sc.key" type="button"
        :aria-pressed="s.scenario === sc.key" :data-scenario="sc.key"
        @click="s.scenario = sc.key"
      >{{ sc.label }}</button>
    </fieldset>
    <p class="describe">{{ current.describe }}</p>

    <div class="controls">
      <label :for="`${id}-limit`">Limit <output>{{ s.limit }} a second</output></label>
      <input :id="`${id}-limit`" v-model.number="s.limit" type="range" min="5" max="20" step="1" />
      <label :for="`${id}-burst`">Token bucket size <output>{{ s.burst }}</output></label>
      <input :id="`${id}-burst`" v-model.number="s.burst" type="range" min="1" max="30" step="1" />
    </div>

    <figure class="chart">
      <figcaption>
        <span class="key"><span class="swatch accepted" />Accepted</span>
        <span class="key"><span class="swatch refused" />Refused (429)</span>
        <span>per 100 ms</span>
      </figcaption>
      <div v-for="row in rows" :key="row.key" class="strip" :data-row="row.key">
        <span class="row-label">{{ row.label }}</span>
        <svg :viewBox="`0 0 ${W} ${ROW_H}`" preserveAspectRatio="none" role="img"
          :aria-label="`${row.label}: ${row.accepted} accepted, ${row.refused} refused`">
          <line class="base" x1="0" :x2="W" :y1="ROW_H - 1" :y2="ROW_H - 1" />
          <template v-for="(slot, i) in row.slots" :key="i">
            <rect v-if="slot.accepted" class="accepted" :x="x(i)" :width="barW" :y="ROW_H - 1 - h(slot.accepted)" :height="h(slot.accepted)" />
            <rect v-if="slot.refused" class="refused" :x="x(i)" :width="barW" :y="ROW_H - 1 - h(slot.accepted) - h(slot.refused) - (slot.accepted ? 1 : 0)" :height="h(slot.refused)" />
          </template>
        </svg>
      </div>
      <div class="axis"><span>0 s</span><span>1 s</span><span>2 s</span><span>3 s</span><span>4 s</span></div>
    </figure>

    <table class="results">
      <thead>
        <tr><th scope="col">Limiter</th><th scope="col">Accepted</th><th scope="col">Refused</th><th scope="col">Most accepted in any one second</th></tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.key" :data-algorithm="row.key">
          <th scope="row">{{ row.label }}</th>
          <td data-col="accepted">{{ row.accepted }}</td>
          <td data-col="refused">{{ row.refused }}</td>
          <td data-col="busiest" :class="{ over: row.busiest > s.limit }">{{ row.busiest }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.simulator {
  --accent: #2a78d6;
  --context: #83827d;
  margin: 16px 0;
  padding: 12px 16px 16px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background-color: var(--vp-c-bg-soft);
  font-size: 14px;
}

:global(.dark) .simulator {
  --accent: #3987e5;
  --context: #6b6a65;
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
.describe {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.describe {
  margin: 6px 0;
}

.choices {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 0;
  padding: 0;
  border: 0;
}

.choices legend {
  margin-bottom: 4px;
  font-weight: 500;
}

.choices button {
  padding: 4px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 16px;
  background-color: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font-size: 13px;
  cursor: pointer;
}

.choices button[aria-pressed='true'] {
  border-color: var(--vp-c-brand-1);
  background-color: var(--vp-c-brand-soft);
  color: var(--vp-c-brand-1);
}

.controls {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr);
  gap: 6px 16px;
  align-items: center;
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

.chart {
  margin: 12px 0 0;
}

.chart figcaption {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.key {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.swatch {
  display: inline-block;
  width: 12px;
  height: 12px;
  border-radius: 2px;
}

.swatch.accepted,
.accepted {
  background-color: var(--accent);
  fill: var(--accent);
}

.swatch.refused,
.refused {
  background-color: var(--context);
  fill: var(--context);
}

.strip {
  margin-top: 6px;
}

.row-label {
  font-size: 13px;
}

.strip svg {
  display: block;
  width: 100%;
  height: 56px;
}

.base {
  stroke: var(--vp-c-divider);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}

.axis {
  display: flex;
  justify-content: space-between;
  margin-top: 2px;
  font-size: 11px;
  color: var(--vp-c-text-2);
  font-variant-numeric: tabular-nums;
}

.results {
  display: table;
  width: 100%;
  margin: 12px 0 0;
  border-collapse: collapse;
  font-size: 14px;
}

.results th,
.results td {
  padding: 6px 10px;
  border: 1px solid var(--vp-c-divider);
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.results th[scope='row'],
.results thead th:first-child {
  text-align: left;
  font-weight: 500;
}

.results td.over {
  color: var(--vp-c-danger-1);
  font-weight: 600;
}

button:focus-visible,
input:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
