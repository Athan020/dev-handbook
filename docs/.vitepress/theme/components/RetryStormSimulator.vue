<script setup lang="ts">
// Retry storm simulator for B4 stage 2.
//
// A model of the stage's lab: transfers arrive at a steady rate and each needs a decision from
// a fraud service with a fixed capacity and a queue. A caller gives up on an attempt after its
// timeout and, depending on the retry policy, tries again. A blip makes the service slow for a
// few seconds. By default the service still works through attempts whose callers have given up
// (it can't tell), which is what lets retries keep it overloaded after the blip is over.
// Deterministic: the same settings always give the same run.
import { computed, reactive, ref, useId } from 'vue'

type Policy = 'none' | 'immediate' | 'backoff' | 'jitter' | 'budget'

const SECONDS = 50
const DEMAND = 150 // transfers a second
const DT = 0.1
const BLIP_AT = 15
const TIMEOUT = 0.5
const QUEUE_LIMIT = 2000
const SLOW_CAPACITY = 10

const policies: { key: Policy; label: string }[] = [
  { key: 'none', label: 'No retries' },
  { key: 'immediate', label: '3 immediate retries' },
  { key: 'backoff', label: '3 retries, exponential backoff' },
  { key: 'jitter', label: '3 retries, backoff and jitter' },
  { key: 'budget', label: '3 retries, 10% budget' },
]

const id = useId()
const s = reactive({ policy: 'immediate' as Policy, headroom: 133, blipSeconds: 3, finishesAbandoned: true })

function seeded(seed: number) {
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

interface Attempt { attempt: number; sentAt: number; abandoned: boolean }

function simulate(policy: Policy, capacity: number, blipSeconds: number, finishesAbandoned: boolean) {
  const rng = seeded(11)
  const ticks = SECONDS / DT
  const sec = (t: number) => Math.floor(t * DT)
  const calls = Array(SECONDS).fill(0)
  const good = Array(SECONDS).fill(0)
  const failed = Array(SECONDS).fill(0)
  const queue: Attempt[] = []
  const scheduled = new Map<number, number[]>()
  const budgetLog: { t: number; retry: boolean }[] = []
  const retries = policy === 'none' ? 0 : 3
  let credit = 0

  const slow = (t: number) => t * DT >= BLIP_AT && t * DT < BLIP_AT + blipSeconds

  function allowRetry(t: number) {
    if (policy !== 'budget') return true
    while (budgetLog.length && budgetLog[0].t < t - 10 / DT) budgetLog.shift()
    const originals = budgetLog.filter((b) => !b.retry).length
    if (budgetLog.length - originals >= 10 + originals * 0.1) return false
    budgetLog.push({ t, retry: true })
    return true
  }

  function fail(attempt: number, t: number) {
    if (attempt <= retries && allowRetry(t)) {
      const base = policy === 'backoff' || policy === 'jitter' ? 0.2 * 2 ** (attempt - 1) : 0
      const delay = policy === 'jitter' ? base * rng() : base
      const at = t + Math.max(1, Math.round(delay / DT))
      const list = scheduled.get(at) ?? []
      list.push(attempt + 1)
      scheduled.set(at, list)
    } else {
      failed[sec(t)]++
    }
  }

  function send(attempt: number, t: number) {
    calls[sec(t)]++
    if (queue.length >= QUEUE_LIMIT) return fail(attempt, t)
    queue.push({ attempt, sentAt: t, abandoned: false })
  }

  for (let t = 0; t < ticks; t++) {
    const arrivals = Math.round((t + 1) * DEMAND * DT) - Math.round(t * DEMAND * DT)
    for (let i = 0; i < arrivals; i++) {
      if (policy === 'budget') budgetLog.push({ t, retry: false })
      send(1, t)
    }
    for (const attempt of scheduled.get(t) ?? []) send(attempt, t)
    scheduled.delete(t)

    credit += (slow(t) ? SLOW_CAPACITY : capacity) * DT
    while (credit >= 1 && queue.length) {
      const a = queue.shift()!
      if (a.abandoned && !finishesAbandoned) continue // dropped without using capacity
      credit--
      if (!a.abandoned) good[sec(t)]++
    }
    if (!queue.length) credit = Math.min(credit, 1)

    for (const a of queue) {
      if (!a.abandoned && t - a.sentAt >= TIMEOUT / DT) {
        a.abandoned = true
        fail(a.attempt, t)
      }
    }
  }

  const recovered = good.findIndex((g, i) => i >= BLIP_AT + blipSeconds && g >= 0.95 * DEMAND)
  return {
    calls,
    good,
    recoveredAfter: recovered < 0 ? null : recovered - BLIP_AT,
    failed: failed.reduce((a, b) => a + b, 0),
    peakCalls: Math.max(...calls),
  }
}

const capacity = computed(() => Math.round((DEMAND * s.headroom) / 100))
const run = computed(() => simulate(s.policy, capacity.value, s.blipSeconds, s.finishesAbandoned))

// Chart
const W = 600
const H = 220
const PAD = { left: 48, right: 12, top: 12, bottom: 28 }
const plotW = W - PAD.left - PAD.right
const plotH = H - PAD.top - PAD.bottom
const yMax = computed(() => niceMax(Math.max(...run.value.calls, capacity.value, DEMAND)))
const x = (i: number) => PAD.left + (i / (SECONDS - 1)) * plotW
const y = (v: number) => PAD.top + plotH - (v / yMax.value) * plotH

function niceMax(v: number) {
  const step = 10 ** Math.floor(Math.log10(v)) / 2
  return Math.ceil(v / step) * step
}

const line = (values: number[]) => 'M' + values.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`).join('L')
const yTicks = computed(() => [0, 0.5, 1].map((f) => Math.round(f * yMax.value)))
const xTicks = [0, 10, 20, 30, 40, 49]

const hover = ref<number | null>(null)
function onMove(event: PointerEvent) {
  const svg = event.currentTarget as SVGSVGElement
  const rect = svg.getBoundingClientRect()
  const px = ((event.clientX - rect.left) / rect.width) * W
  const i = Math.round(((px - PAD.left) / plotW) * (SECONDS - 1))
  hover.value = i >= 0 && i < SECONDS ? i : null
}

const fmt = (n: number) => Math.round(n).toLocaleString('en-GB')
</script>

<template>
  <div class="simulator retry-storm">
    <div class="header">
      <span class="title">Retry storm simulator</span>
      <span class="note">{{ DEMAND }} transfers a second; the fraud service is slow for a while from {{ BLIP_AT }} s</span>
    </div>

    <fieldset class="choices">
      <legend>Retry policy</legend>
      <button
        v-for="p in policies" :key="p.key" type="button"
        :aria-pressed="s.policy === p.key" :data-policy="p.key"
        @click="s.policy = p.key"
      >{{ p.label }}</button>
    </fieldset>

    <div class="controls">
      <label :for="`${id}-headroom`">Fraud service capacity, as a share of demand <output>{{ s.headroom }}%</output></label>
      <input :id="`${id}-headroom`" v-model.number="s.headroom" type="range" min="110" max="500" step="1" />

      <label :for="`${id}-blip`">Slow for <output>{{ s.blipSeconds }} s</output></label>
      <input :id="`${id}-blip`" v-model.number="s.blipSeconds" type="range" min="1" max="10" step="1" />

      <label class="check" :for="`${id}-abandoned`">
        <input :id="`${id}-abandoned`" v-model="s.finishesAbandoned" type="checkbox" />
        The service finishes requests whose callers have given up
      </label>
    </div>

    <figure class="chart">
      <figcaption>
        <span class="key"><span class="swatch good" />Transfers succeeding per second</span>
        <span class="key"><span class="swatch calls" />Calls reaching the fraud service per second</span>
        <span class="key"><span class="swatch cap" />Its capacity</span>
      </figcaption>
      <svg
        :viewBox="`0 0 ${W} ${H}`" role="img"
        :aria-label="`Over ${SECONDS} seconds, calls to the fraud service peak at ${fmt(run.peakCalls)} a second`"
        @pointermove="onMove" @pointerleave="hover = null"
      >
        <g class="grid">
          <line v-for="t in yTicks" :key="`g${t}`" :x1="PAD.left" :x2="W - PAD.right" :y1="y(t)" :y2="y(t)" />
        </g>
        <g class="axis">
          <text v-for="t in yTicks" :key="`y${t}`" :x="PAD.left - 6" :y="y(t) + 4" text-anchor="end">{{ fmt(t) }}</text>
          <text v-for="t in xTicks" :key="`x${t}`" :x="x(t)" :y="H - 8" :text-anchor="t === 49 ? 'end' : 'middle'">{{ t === 49 ? '50 s' : `${t} s` }}</text>
        </g>
        <rect class="blip" :x="x(BLIP_AT)" :y="PAD.top" :width="x(BLIP_AT + s.blipSeconds) - x(BLIP_AT)" :height="plotH" />
        <line class="cap" :x1="PAD.left" :x2="W - PAD.right" :y1="y(capacity)" :y2="y(capacity)" />
        <path class="calls" :d="line(run.calls)" />
        <path class="good" :d="line(run.good)" />
        <g v-if="hover !== null">
          <line class="cross" :x1="x(hover)" :x2="x(hover)" :y1="PAD.top" :y2="PAD.top + plotH" />
          <circle class="dot" :cx="x(hover)" :cy="y(run.good[hover])" r="4" />
        </g>
      </svg>
      <div v-if="hover !== null" class="tooltip" role="status">
        <strong>{{ hover }} s</strong>: {{ fmt(run.good[hover]) }} transfers succeeded;
        {{ fmt(run.calls[hover]) }} calls reached the fraud service (capacity {{ fmt(capacity) }})
      </div>
      <p class="meta">Shaded: the seconds the fraud service is slow.</p>
    </figure>

    <dl class="stats">
      <div>
        <dt>Recovered</dt>
        <dd data-stat="recovered" :class="{ bad: run.recoveredAfter === null }">
          {{ run.recoveredAfter === null ? 'not within 50 s' : `${run.recoveredAfter} s after the blip began` }}
        </dd>
      </div>
      <div><dt>Transfers that failed</dt><dd data-stat="failed">{{ fmt(run.failed) }}</dd></div>
      <div><dt>Most calls in a second</dt><dd data-stat="peak">{{ fmt(run.peakCalls) }} ({{ (run.peakCalls / DEMAND).toFixed(1) }}× demand)</dd></div>
    </dl>
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
.meta {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.meta {
  margin: 4px 0 0;
}

.choices {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 0 0 8px;
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

.controls label.check {
  grid-column: 1 / -1;
  justify-content: flex-start;
}

.controls output {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.controls input[type='range'] {
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
  width: 16px;
  height: 0;
  border-top: 2px solid;
}

.swatch.good {
  border-color: var(--accent);
}

.swatch.calls {
  border-color: var(--context);
}

.swatch.cap {
  border-top: 1px dashed var(--vp-c-text-2);
}

.chart svg {
  display: block;
  width: 100%;
  height: auto;
  margin-top: 4px;
  touch-action: none;
}

.grid line {
  stroke: var(--vp-c-divider);
  stroke-width: 1;
}

.axis text {
  fill: var(--vp-c-text-2);
  font-size: 11px;
  font-variant-numeric: tabular-nums;
}

.blip {
  fill: var(--vp-c-default-soft);
}

.cap {
  stroke: var(--vp-c-text-2);
  stroke-width: 1;
  stroke-dasharray: 4 3;
}

.calls,
.good {
  fill: none;
  stroke-width: 2;
  stroke-linejoin: round;
}

.calls {
  stroke: var(--context);
}

.good {
  stroke: var(--accent);
}

.cross {
  stroke: var(--vp-c-text-3);
  stroke-width: 1;
}

.dot {
  fill: var(--accent);
  stroke: var(--vp-c-bg-soft);
  stroke-width: 2;
}

.tooltip {
  margin-top: 4px;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 8px;
  margin: 12px 0 0;
}

.stats div {
  padding: 8px 10px;
  border-radius: 6px;
  background-color: var(--vp-c-bg);
}

.stats dt {
  font-size: 12px;
  color: var(--vp-c-text-2);
}

.stats dd {
  margin: 2px 0 0;
  font-size: 16px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.stats dd.bad {
  color: var(--vp-c-danger-1);
}

button:focus-visible,
input:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
