<script setup lang="ts">
// Cache stampede simulator for B1 stage 5.
//
// One popular cache entry, such as a busy account's summary. Requests arrive at random;
// a hit answers in about 1 ms. When the entry has expired, what happens depends on the
// strategy:
//   - every: each request that misses starts its own recompute (plain cache-aside);
//   - coalesce: the first miss recomputes and later ones wait for its result;
//   - stale: requests get the expired value at once, and one background recompute refreshes it.
// Recomputes are database queries: they go through a 20-connection pool and share 8 cores,
// as in the lab. The simulation runs in 1 ms steps, in real time, with a seeded random
// number generator, so a given setup always behaves the same way.
import { computed, onBeforeUnmount, onMounted, reactive, ref, useId } from 'vue'

type Strategy = 'every' | 'coalesce' | 'stale'
interface Settings {
  rate: number // requests per second for this entry
  ttlS: number // how long an entry stays fresh
  recomputeMs: number // query time with a core to itself
  strategy: Strategy
}

const POOL = 20
const CORES = 8
const HIT_MS = 1
const WINDOW_MS = 10_000
const HISTORY_MS = 30_000

const strategies: Record<Strategy, string> = {
  every: 'Every miss recomputes',
  coalesce: 'One recompute, others wait',
  stale: 'Serve stale while refreshing',
}

const presets: Record<string, { label: string; settings: Settings }> = {
  every: { label: 'Every miss recomputes', settings: { rate: 300, ttlS: 5, recomputeMs: 700, strategy: 'every' } },
  coalesce: { label: 'One recompute, others wait', settings: { rate: 300, ttlS: 5, recomputeMs: 700, strategy: 'coalesce' } },
  stale: { label: 'Serve stale while refreshing', settings: { rate: 300, ttlS: 5, recomputeMs: 700, strategy: 'stale' } },
  longTtl: { label: 'One recompute, 30 s TTL', settings: { rate: 300, ttlS: 30, recomputeMs: 700, strategy: 'coalesce' } },
}

const id = useId()
const s = reactive<Settings>({ ...presets.every.settings })
const preset = ref('every')

interface Query { remaining: number; waiters: number[] } // waiters: arrival times of requests answered by this query
let rng = seeded(1)
let now = 0
let nextArrival = 0
let value: { computedAt: number } | null = null
let running: Query[] = []
let poolQueue: Query[] = []
let latencies: { at: number; ms: number; age: number }[] = []
let queryStarts: number[] = []
let history: { at: number; running: number }[] = []
let peakRunning = 0
let slowest = 0
let oldest = 0

const view = reactive({
  simSeconds: 0,
  running: 0,
  peak: 0,
  queriesPerS: 0,
  p50: 0,
  p99: 0,
  slowest: 0,
  oldest: 0,
  history: [] as number[],
})

function seeded(seed: number) {
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const exponential = (meanMs: number) => -Math.log(1 - rng()) * meanMs

function reset() {
  rng = seeded(1)
  now = 0
  nextArrival = exponential(1000 / s.rate)
  // Start with a freshly computed value, as if the cache had just been filled.
  value = { computedAt: 0 }
  running = []
  poolQueue = []
  latencies = []
  queryStarts = []
  history = []
  peakRunning = 0
  slowest = 0
  oldest = 0
  refresh()
}

function startQuery(waiters: number[]): Query {
  const query = { remaining: s.recomputeMs * (0.8 + 0.4 * rng()), waiters }
  queryStarts.push(now)
  if (running.length < POOL) running.push(query)
  else poolQueue.push(query)
  return query
}

function answer(arrival: number, age: number) {
  const ms = now - arrival + HIT_MS
  latencies.push({ at: now, ms, age })
  slowest = Math.max(slowest, ms)
  oldest = Math.max(oldest, age)
}

function step() {
  now += 1
  const fresh = value !== null && now - value.computedAt < s.ttlS * 1000

  while (nextArrival <= now) {
    const arrival = nextArrival
    nextArrival += exponential(1000 / s.rate)
    if (fresh) {
      answer(arrival, now - value!.computedAt)
    } else if (s.strategy === 'every') {
      startQuery([arrival])
    } else {
      const inFlight = running[0] ?? poolQueue[0]
      if (s.strategy === 'stale' && value) {
        answer(arrival, now - value.computedAt) // old value, straight away
        if (!inFlight) startQuery([])
      } else if (inFlight) {
        inFlight.waiters.push(arrival)
      } else {
        startQuery([arrival])
      }
    }
  }

  // Queries share the database's cores when more are running than there are cores.
  const speed = running.length > CORES ? CORES / running.length : 1
  for (const query of running) query.remaining -= speed
  for (let i = running.length - 1; i >= 0; i--) {
    if (running[i].remaining <= 0) {
      const [done] = running.splice(i, 1)
      value = { computedAt: now }
      for (const arrival of done.waiters) answer(arrival, 0)
      if (poolQueue.length) running.push(poolQueue.shift()!)
    }
  }

  peakRunning = Math.max(peakRunning, running.length + poolQueue.length)
  if (now % 100 === 0) history.push({ at: now, running: running.length + poolQueue.length })
}

function refresh() {
  const from = now - WINDOW_MS
  while (latencies.length && latencies[0].at < from) latencies.shift()
  while (queryStarts.length && queryStarts[0] < from) queryStarts.shift()
  while (history.length && history[0].at < now - HISTORY_MS) history.shift()

  const seconds = Math.min(now, WINDOW_MS) / 1000 || 1
  const sorted = latencies.map((l) => l.ms).sort((a, b) => a - b)
  const pct = (p: number) => (sorted.length ? sorted[Math.min(sorted.length - 1, Math.ceil(p * sorted.length) - 1)] : 0)

  view.simSeconds = Math.floor(now / 1000)
  view.running = running.length + poolQueue.length
  view.peak = peakRunning
  view.queriesPerS = Math.round((queryStarts.length / seconds) * 10) / 10
  view.p50 = pct(0.5)
  view.p99 = pct(0.99)
  view.slowest = slowest
  view.oldest = oldest
  view.history = history.map((h) => h.running)
}

let frame = 0
let last = 0
function tick(time: number) {
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
const sparkMax = computed(() => Math.max(5, ...view.history))
const sparkline = computed(() => {
  const points = view.history
  if (points.length < 2) return ''
  return points.map((q, i) => `${(i / (HISTORY_MS / 100 - 1)) * 300},${40 - (q / sparkMax.value) * 38}`).join(' ')
})
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Cache stampede simulator</span>
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
      <input :id="`${id}-rate`" v-model.number="s.rate" type="range" min="10" max="1000" step="10" @input="changed" />

      <label :for="`${id}-ttl`">Time to live <output>{{ s.ttlS }} s</output></label>
      <input :id="`${id}-ttl`" v-model.number="s.ttlS" type="range" min="1" max="60" step="1" @input="changed" />

      <label :for="`${id}-recompute`">Recompute time <output>{{ s.recomputeMs }} ms</output></label>
      <input :id="`${id}-recompute`" v-model.number="s.recomputeMs" type="range" min="50" max="2000" step="50" @input="changed" />

      <label :for="`${id}-strategy`">When the entry expires</label>
      <select :id="`${id}-strategy`" v-model="s.strategy" @change="changed">
        <option v-for="(label, key) in strategies" :key="key" :value="key">{{ label }}</option>
      </select>
    </div>

    <dl class="stats">
      <div><dt>Queries started per second</dt><dd data-stat="queries">{{ view.queriesPerS }}</dd></div>
      <div><dt>Recomputes running now</dt><dd data-stat="running">{{ view.running }}</dd></div>
      <div><dt>Most at once</dt><dd data-stat="peak">{{ view.peak }}</dd></div>
      <div><dt>p99 latency</dt><dd data-stat="p99">{{ formatMs(view.p99) }}</dd></div>
      <div><dt>Slowest request</dt><dd data-stat="slowest">{{ formatMs(view.slowest) }}</dd></div>
      <div><dt>Oldest data served</dt><dd data-stat="oldest">{{ formatMs(view.oldest) }}</dd></div>
    </dl>
    <p class="meta">
      Rates and p99 cover the last 10 simulated seconds; "Most at once", "Slowest request" and "Oldest data
      served" cover the whole run. Recomputes use a pool of {{ POOL }}
      connections on a database with {{ CORES }} cores; queries beyond 8 share the cores.
    </p>

    <figure class="history">
      <svg viewBox="0 0 300 42" preserveAspectRatio="none" role="img" :aria-label="`Recomputes running over the last 30 seconds, up to ${sparkMax}`">
        <polyline :points="sparkline" fill="none" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" />
      </svg>
      <figcaption>Recomputes running, last 30 seconds (top of chart: {{ sparkMax }})</figcaption>
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

.controls select {
  padding: 4px 8px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font: inherit;
  font-size: 14px;
}

@media (max-width: 520px) {
  .controls {
    grid-template-columns: 1fr;
  }
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
input:focus-visible,
select:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
