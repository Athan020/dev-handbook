<script setup lang="ts">
// Connection pool simulator for B1 stage 4.
//
// Requests arrive at random (a Poisson process) and wait in a queue for a pooled database
// connection. Each query needs some amount of database CPU; when more queries run at once
// than the database has cores, they share the cores and each one slows down. A request
// that waits longer than the timeout fails. The simulation runs in 1 ms steps, in real
// time, with a seeded random number generator so a given setup always behaves the same.
import { computed, onBeforeUnmount, onMounted, reactive, ref, useId } from 'vue'

interface Settings {
  rate: number // requests per second
  queryMs: number // query time with a core to itself
  pool: number // connections in the pool
  cores: number // database CPU cores
  timeoutS: number // how long a request waits for a connection
}

const presets: Record<string, { label: string; settings: Settings }> = {
  comfortable: { label: 'Comfortable', settings: { rate: 100, queryMs: 50, pool: 10, cores: 8, timeoutS: 15 } },
  near: { label: 'Near capacity', settings: { rate: 150, queryMs: 50, pool: 10, cores: 8, timeoutS: 15 } },
  overloaded: { label: 'Overloaded', settings: { rate: 200, queryMs: 50, pool: 10, cores: 8, timeoutS: 15 } },
  failFast: { label: 'Overloaded, short timeout', settings: { rate: 200, queryMs: 50, pool: 10, cores: 8, timeoutS: 1 } },
  biggerPool: { label: 'Overloaded, pool of 50', settings: { rate: 200, queryMs: 50, pool: 50, cores: 8, timeoutS: 15 } },
}

const id = useId()
const s = reactive<Settings>({ ...presets.comfortable.settings })
const preset = ref('comfortable')

const WINDOW_MS = 5000
const HISTORY_MS = 30_000

// Simulation state, kept outside Vue's reactivity for speed; `view` is refreshed each frame.
let rng = seeded(1)
let now = 0
let nextArrival = 0
let queue: number[] = [] // arrival times
let active: { arrival: number; remaining: number }[] = []
let completed: { at: number; latency: number }[] = []
let failed: number[] = []
let history: { at: number; queue: number }[] = []

const view = reactive({
  simSeconds: 0,
  busy: 0,
  queued: 0,
  completedPerS: 0,
  failedPerS: 0,
  p50: 0,
  p99: 0,
  history: [] as number[],
})

/** Offered load as a share of what the database can actually do. */
const utilisation = computed(() => (s.rate * s.queryMs) / 1000 / Math.min(s.pool, s.cores))
const capacity = computed(() => Math.round((Math.min(s.pool, s.cores) * 1000) / s.queryMs))

function seeded(seed: number) {
  // mulberry32: small, fast and good enough for a simulation.
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function exponential(meanMs: number) {
  return -Math.log(1 - rng()) * meanMs
}

function reset() {
  rng = seeded(1)
  now = 0
  nextArrival = exponential(1000 / s.rate)
  queue = []
  active = []
  completed = []
  failed = []
  history = []
  refresh()
}

function step() {
  now += 1

  while (nextArrival <= now) {
    queue.push(nextArrival)
    nextArrival += exponential(1000 / s.rate)
  }

  const deadline = now - s.timeoutS * 1000
  while (queue.length && queue[0] < deadline) {
    queue.shift()
    failed.push(now)
  }

  while (queue.length && active.length < s.pool) {
    // Query work varies: half to one and a half times the typical query time.
    active.push({ arrival: queue.shift()!, remaining: s.queryMs * (0.5 + rng()) })
  }

  // Queries share the cores when there are more of them than cores.
  const speed = active.length > s.cores ? s.cores / active.length : 1
  for (const query of active) query.remaining -= speed
  for (let i = active.length - 1; i >= 0; i--) {
    if (active[i].remaining <= 0) {
      completed.push({ at: now, latency: now - active[i].arrival })
      active.splice(i, 1)
    }
  }

  if (now % 100 === 0) history.push({ at: now, queue: queue.length })
}

function refresh() {
  const from = now - WINDOW_MS
  while (completed.length && completed[0].at < from) completed.shift()
  while (failed.length && failed[0] < from) failed.shift()
  while (history.length && history[0].at < now - HISTORY_MS) history.shift()

  const seconds = Math.min(now, WINDOW_MS) / 1000 || 1
  const latencies = completed.map((c) => c.latency).sort((a, b) => a - b)
  const pct = (p: number) => (latencies.length ? latencies[Math.min(latencies.length - 1, Math.ceil(p * latencies.length) - 1)] : 0)

  view.simSeconds = Math.floor(now / 1000)
  view.busy = active.length
  view.queued = queue.length
  view.completedPerS = Math.round(completed.length / seconds)
  view.failedPerS = Math.round(failed.length / seconds)
  view.p50 = pct(0.5)
  view.p99 = pct(0.99)
  view.history = history.map((h) => h.queue)
}

let frame = 0
let last = 0
function tick(time: number) {
  // Simulate the real time that has passed, capped so a background tab doesn't stall.
  const elapsed = last ? Math.min(time - last, 100) : 16
  last = time
  for (let i = 0; i < Math.round(elapsed); i++) step()
  refresh()
  frame = requestAnimationFrame(tick)
}

function applyPreset(key: string) {
  preset.value = key
  Object.assign(s, presets[key].settings)
  reset()
}

function changed() {
  preset.value = ''
  reset()
}

onMounted(() => {
  reset()
  frame = requestAnimationFrame(tick)
})
onBeforeUnmount(() => cancelAnimationFrame(frame))

const formatMs = (ms: number) => (ms >= 1000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms)} ms`)

const sparkline = computed(() => {
  const points = view.history
  if (points.length < 2) return ''
  const max = Math.max(10, ...points)
  return points.map((q, i) => `${(i / (HISTORY_MS / 100 - 1)) * 300},${40 - (q / max) * 38}`).join(' ')
})
const sparkMax = computed(() => Math.max(10, ...view.history))
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Connection pool simulator</span>
      <span class="note">Simulated time: {{ view.simSeconds }} s</span>
    </div>

    <div class="presets" role="group" aria-label="Scenarios">
      <button
        v-for="(p, key) in presets"
        :key="key"
        type="button"
        :aria-pressed="preset === key"
        @click="applyPreset(key)"
      >{{ p.label }}</button>
    </div>

    <div class="controls">
      <label :for="`${id}-rate`">Requests per second <output>{{ s.rate }}</output></label>
      <input :id="`${id}-rate`" v-model.number="s.rate" type="range" min="10" max="400" step="10" @input="changed" />

      <label :for="`${id}-query`">Query time <output>{{ s.queryMs }} ms</output></label>
      <input :id="`${id}-query`" v-model.number="s.queryMs" type="range" min="5" max="200" step="5" @input="changed" />

      <label :for="`${id}-pool`">Pool size <output>{{ s.pool }}</output></label>
      <input :id="`${id}-pool`" v-model.number="s.pool" type="range" min="1" max="60" step="1" @input="changed" />

      <label :for="`${id}-cores`">Database cores <output>{{ s.cores }}</output></label>
      <input :id="`${id}-cores`" v-model.number="s.cores" type="range" min="1" max="32" step="1" @input="changed" />

      <label :for="`${id}-timeout`">Wait timeout <output>{{ s.timeoutS }} s</output></label>
      <input :id="`${id}-timeout`" v-model.number="s.timeoutS" type="range" min="0.5" max="30" step="0.5" @input="changed" />
    </div>

    <p class="load" :class="{ over: utilisation >= 1, near: utilisation >= 0.8 && utilisation < 1 }">
      Offered load: <strong>{{ Math.round(utilisation * 100) }}%</strong> of capacity
      (the database can finish about {{ capacity }} queries a second with this pool and these cores)
    </p>

    <div class="pool" :aria-label="`${view.busy} of ${s.pool} connections busy`" role="img">
      <span v-for="n in s.pool" :key="n" class="slot" :class="{ busy: n <= view.busy, shared: n <= view.busy && view.busy > s.cores }" />
    </div>
    <p class="legend">
      <span class="slot busy" /> busy <span class="slot shared" /> busy, sharing a core <span class="slot" /> idle
    </p>

    <dl class="stats" aria-live="off">
      <div><dt>Waiting for a connection</dt><dd data-stat="queued">{{ view.queued }}</dd></div>
      <div><dt>Completed per second</dt><dd data-stat="completed">{{ view.completedPerS }}</dd></div>
      <div><dt>Timed out per second</dt><dd data-stat="failed" :class="{ bad: view.failedPerS > 0 }">{{ view.failedPerS }}</dd></div>
      <div><dt>p50 latency</dt><dd data-stat="p50">{{ formatMs(view.p50) }}</dd></div>
      <div><dt>p99 latency</dt><dd data-stat="p99">{{ formatMs(view.p99) }}</dd></div>
    </dl>
    <p class="meta">Rates and latencies cover the last 5 simulated seconds. Latency is waiting plus query time.</p>

    <figure class="history">
      <svg viewBox="0 0 300 42" preserveAspectRatio="none" role="img" :aria-label="`Queue length over the last 30 seconds, up to ${sparkMax}`">
        <polyline :points="sparkline" fill="none" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" />
      </svg>
      <figcaption>Queue length, last 30 seconds (top of chart: {{ sparkMax }})</figcaption>
    </figure>

    <button type="button" class="link" @click="reset">Restart</button>
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

.note,
.meta,
.legend,
figcaption {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.presets {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 12px;
}

.presets button {
  padding: 4px 10px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 999px;
  background-color: var(--vp-c-bg);
  font-size: 13px;
}

.presets button[aria-pressed='true'] {
  border-color: var(--vp-c-brand-1);
  background-color: var(--vp-c-brand-soft);
  color: var(--vp-c-brand-1);
}

.controls {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr);
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

.load {
  margin: 12px 0 8px;
  font-size: 14px;
}

.load.near strong {
  color: var(--vp-c-warning-1);
}

.load.over strong {
  color: var(--vp-c-danger-1);
}

.pool {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
}

.slot {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 3px;
  background-color: var(--vp-c-bg);
  vertical-align: middle;
}

.slot.busy {
  border-color: var(--vp-c-brand-2);
  background-color: var(--vp-c-brand-2);
}

.slot.shared {
  border-color: var(--vp-c-warning-2);
  background-color: var(--vp-c-warning-2);
}

.legend {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 6px;
  margin: 6px 0 0;
}

.legend .slot {
  margin-left: 8px;
}

.legend .slot:first-child {
  margin-left: 0;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
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
  font-size: 18px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.stats dd.bad {
  color: var(--vp-c-danger-1);
}

.meta {
  margin: 6px 0 0;
}

.history {
  margin: 12px 0 0;
  color: var(--vp-c-brand-1);
}

.history svg {
  display: block;
  width: 100%;
  height: 48px;
  border-bottom: 1px solid var(--vp-c-divider);
}

figcaption {
  margin-top: 4px;
}

.link {
  margin-top: 8px;
  font-size: 13px;
  color: var(--vp-c-text-2);
  text-decoration: underline;
  text-underline-offset: 2px;
}

button:focus-visible,
input:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
