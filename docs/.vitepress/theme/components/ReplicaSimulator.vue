<script setup lang="ts">
// Read replica simulator for B1 stage 6.
//
// Customers record transactions on the primary. The replica applies each one after the
// current replication lag. Some customers then read their account back shortly after
// writing (a "read-back"); everyone else's reads are unrelated to their own writes.
// The routing policy decides which database serves each read. A read-back that the replica
// serves before it has applied the customer's own transaction is "stale": the customer
// can't see the payment they just made.
//
// Time-stepped (10 ms), in real time, with a seeded random number generator.
import { computed, onBeforeUnmount, onMounted, reactive, ref, useId } from 'vue'

type Policy = 'primary' | 'replica' | 'sticky' | 'token'
interface Settings {
  lagMs: number // typical replication lag
  spikes: boolean // occasional lag spikes
  readBackS: number // mean time before a customer reads back after writing
  stickyS: number // sticky window for the 'sticky' policy
  policy: Policy
}

const STEP = 10
const WRITES_PER_S = 20
const OTHER_READS_PER_S = 180
const READ_BACK_SHARE = 0.5 // half of writers read back
const WINDOW_MS = 20_000
const HISTORY_MS = 30_000

const policies: Record<Policy, string> = {
  primary: 'Primary for everything',
  replica: 'Replica for everything',
  sticky: 'Primary for a while after a write',
  token: 'Consistency token',
}

const presets: Record<string, { label: string; settings: Settings }> = {
  replica: { label: 'Replica for everything', settings: { lagMs: 500, spikes: false, readBackS: 1, stickyS: 5, policy: 'replica' } },
  primary: { label: 'Primary for everything', settings: { lagMs: 500, spikes: false, readBackS: 1, stickyS: 5, policy: 'primary' } },
  sticky: { label: 'Sticky for 5 s', settings: { lagMs: 500, spikes: false, readBackS: 1, stickyS: 5, policy: 'sticky' } },
  stickySpikes: { label: 'Sticky for 5 s, with lag spikes', settings: { lagMs: 500, spikes: true, readBackS: 3, stickyS: 5, policy: 'sticky' } },
  token: { label: 'Token, with lag spikes', settings: { lagMs: 500, spikes: true, readBackS: 3, stickyS: 5, policy: 'token' } },
}

const id = useId()
const s = reactive<Settings>({ ...presets.replica.settings })
const preset = ref('replica')

interface ReadBack { at: number; writtenAt: number; appliedAt: number }
let rng = seeded(1)
let now = 0
let nextWrite = 0
let nextOtherRead = 0
let pendingReadBacks: ReadBack[] = []
let reads: { at: number; primary: boolean; readBack: boolean; stale: boolean }[] = []
let lagHistory: { at: number; lag: number }[] = []
let spikeUntil = -1
let nextSpike = 0

const view = reactive({
  simSeconds: 0,
  currentLag: 0,
  readsPerS: 0,
  primaryShare: 0,
  readBacks: 0,
  staleReadBacks: 0,
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

function currentLag() {
  // Lag wobbles around its typical value, and with spikes enabled, jumps to 20× for 3 s
  // roughly every 12 s (a long query on the replica, a burst of writes, a slow network).
  const base = s.lagMs * (0.7 + 0.6 * rng())
  return now < spikeUntil ? base * 20 : base
}

function reset() {
  rng = seeded(1)
  now = 0
  nextWrite = exponential(1000 / WRITES_PER_S)
  nextOtherRead = exponential(1000 / OTHER_READS_PER_S)
  pendingReadBacks = []
  reads = []
  lagHistory = []
  spikeUntil = -1
  nextSpike = 4000 + rng() * 4000
  refresh()
}

function route(readBack: ReadBack | null): boolean {
  // Returns true if the primary serves this read.
  switch (s.policy) {
    case 'primary':
      return true
    case 'replica':
      return false
    case 'sticky':
      return readBack !== null && now - readBack.writtenAt < s.stickyS * 1000
    case 'token':
      return readBack !== null && now < readBack.appliedAt
  }
}

function step() {
  now += STEP
  if (s.spikes && now >= nextSpike) {
    spikeUntil = now + 3000
    nextSpike = now + 9000 + rng() * 6000
  }
  const lag = currentLag()

  while (nextWrite <= now) {
    if (rng() < READ_BACK_SHARE) {
      pendingReadBacks.push({ at: nextWrite + exponential(s.readBackS * 1000), writtenAt: nextWrite, appliedAt: nextWrite + lag })
    }
    nextWrite += exponential(1000 / WRITES_PER_S)
  }

  while (nextOtherRead <= now) {
    reads.push({ at: now, primary: route(null), readBack: false, stale: false })
    nextOtherRead += exponential(1000 / OTHER_READS_PER_S)
  }

  for (let i = pendingReadBacks.length - 1; i >= 0; i--) {
    const rb = pendingReadBacks[i]
    if (rb.at > now) continue
    pendingReadBacks.splice(i, 1)
    const primary = route(rb)
    reads.push({ at: now, primary, readBack: true, stale: !primary && now < rb.appliedAt })
  }

  if (now % 100 === 0) lagHistory.push({ at: now, lag })
}

function refresh() {
  while (reads.length && reads[0].at < now - WINDOW_MS) reads.shift()
  while (lagHistory.length && lagHistory[0].at < now - HISTORY_MS) lagHistory.shift()
  const seconds = Math.min(now, WINDOW_MS) / 1000 || 1
  const readBacks = reads.filter((r) => r.readBack)

  view.simSeconds = Math.floor(now / 1000)
  view.currentLag = lagHistory.length ? lagHistory[lagHistory.length - 1].lag : 0
  view.readsPerS = Math.round(reads.length / seconds)
  view.primaryShare = reads.length ? Math.round((100 * reads.filter((r) => r.primary).length) / reads.length) : 0
  view.readBacks = readBacks.length
  view.staleReadBacks = readBacks.length ? Math.round((100 * readBacks.filter((r) => r.stale).length) / readBacks.length) : 0
  view.history = lagHistory.map((h) => h.lag)
}

let frame = 0
let last = 0
function tick(time: number) {
  const elapsed = last ? Math.min(time - last, 100) : 16
  last = time
  for (let t = 0; t < elapsed; t += STEP) step()
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
const sparkMax = computed(() => Math.max(1000, ...view.history))
const sparkline = computed(() => {
  const points = view.history
  if (points.length < 2) return ''
  return points.map((v, i) => `${(i / (HISTORY_MS / 100 - 1)) * 300},${40 - (v / sparkMax.value) * 38}`).join(' ')
})
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Read replica simulator</span>
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
      <label :for="`${id}-policy`">Where reads go</label>
      <select :id="`${id}-policy`" v-model="s.policy" @change="changed">
        <option v-for="(label, key) in policies" :key="key" :value="key">{{ label }}</option>
      </select>

      <label :for="`${id}-lag`">Typical replication lag <output>{{ formatMs(s.lagMs) }}</output></label>
      <input :id="`${id}-lag`" v-model.number="s.lagMs" type="range" min="0" max="3000" step="50" @input="changed" />

      <label :for="`${id}-readback`">Customers read back after about <output>{{ s.readBackS }} s</output></label>
      <input :id="`${id}-readback`" v-model.number="s.readBackS" type="range" min="0.2" max="10" step="0.2" @input="changed" />

      <label :for="`${id}-sticky`">Sticky window <output>{{ s.stickyS }} s</output></label>
      <input :id="`${id}-sticky`" v-model.number="s.stickyS" type="range" min="1" max="30" step="1" :disabled="s.policy !== 'sticky'" @input="changed" />

      <label :for="`${id}-spikes`">Lag spikes</label>
      <span><input :id="`${id}-spikes`" v-model="s.spikes" type="checkbox" @change="changed" /> occasionally 20× for 3 s</span>
    </div>

    <dl class="stats">
      <div><dt>Replication lag now</dt><dd data-stat="lag">{{ formatMs(view.currentLag) }}</dd></div>
      <div><dt>Reads per second</dt><dd data-stat="reads">{{ view.readsPerS }}</dd></div>
      <div><dt>Reads on the primary</dt><dd data-stat="primary">{{ view.primaryShare }}%</dd></div>
      <div><dt>Read-backs missing the customer's own write</dt><dd data-stat="stale" :class="{ bad: view.staleReadBacks > 0 }">{{ view.staleReadBacks }}%</dd></div>
    </dl>
    <p class="meta">
      {{ WRITES_PER_S }} writes and about {{ OTHER_READS_PER_S }} other reads a second; half of the customers who write
      read their account back soon after. Percentages cover the last 20 simulated seconds ({{ view.readBacks }} read-backs).
    </p>

    <figure class="history">
      <svg viewBox="0 0 300 42" preserveAspectRatio="none" role="img" :aria-label="`Replication lag over the last 30 seconds, up to ${formatMs(sparkMax)}`">
        <polyline :points="sparkline" fill="none" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" />
      </svg>
      <figcaption>Replication lag, last 30 seconds (top of chart: {{ formatMs(sparkMax) }})</figcaption>
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

.controls input[type='range'] {
  width: 100%;
  accent-color: var(--vp-c-brand-1);
}

.controls input[type='checkbox'] {
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
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
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
input:focus-visible,
select:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
