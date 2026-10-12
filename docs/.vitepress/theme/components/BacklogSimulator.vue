<script setup lang="ts">
// Backlog simulator for B2 stage 7.
//
// Two hours of email work, one second at a time. Messages arrive at a steady daily rate, plus a
// payroll batch partway through. Workers send emails; each email takes a fixed time, and the
// provider accepts only so many calls at once, so the most that can be sent per second is
//   min(workers x concurrency, provider limit) x 1000 / email time.
// An optional maximum queue length rejects new messages when the queue is full (backpressure
// to the publisher). The model is fluid and deterministic: no randomness, so the same settings
// always draw the same line. How long a message waited is computed exactly, first in, first out.
import { computed, reactive, ref, useId } from 'vue'

interface Settings {
  normalPerSec: number
  batch: number // payroll messages
  batchMinutes: number // how long the batch takes to arrive
  workers: number
  concurrency: number
  emailMs: number
  providerLimit: number // concurrent calls the provider accepts
  maxQueue: number // 0 = unlimited
}

const MINUTES = 120
const BATCH_START = 30 // minutes
const STEP = 1 // seconds

const presets: Record<string, { label: string; settings: Settings }> = {
  quiet: { label: 'An ordinary day', settings: { normalPerSec: 15, batch: 0, batchMinutes: 10, workers: 1, concurrency: 10, emailMs: 300, providerLimit: 20, maxQueue: 0 } },
  payroll: { label: 'Payroll, one worker', settings: { normalPerSec: 15, batch: 60000, batchMinutes: 10, workers: 1, concurrency: 10, emailMs: 300, providerLimit: 20, maxQueue: 0 } },
  scaled: { label: 'Payroll, at the provider limit', settings: { normalPerSec: 15, batch: 60000, batchMinutes: 10, workers: 2, concurrency: 10, emailMs: 300, providerLimit: 20, maxQueue: 0 } },
  over: { label: 'More workers than the provider allows', settings: { normalPerSec: 15, batch: 60000, batchMinutes: 10, workers: 6, concurrency: 10, emailMs: 300, providerLimit: 20, maxQueue: 0 } },
  bounded: { label: 'Bounded queue', settings: { normalPerSec: 15, batch: 60000, batchMinutes: 10, workers: 1, concurrency: 10, emailMs: 300, providerLimit: 20, maxQueue: 10000 } },
}

const id = useId()
const s = reactive<Settings>({ ...presets.payroll.settings })
const preset = ref('payroll')

function applyPreset(key: string) {
  preset.value = key
  Object.assign(s, presets[key].settings)
}

const capacityPerSec = computed(() => (Math.min(s.workers * s.concurrency, s.providerLimit) * 1000) / s.emailMs)
const refusedShare = computed(() => {
  const attempted = s.workers * s.concurrency
  return attempted > s.providerLimit ? 1 - s.providerLimit / attempted : 0
})

const run = computed(() => {
  const steps = (MINUTES * 60) / STEP
  const batchRate = s.batch / (s.batchMinutes * 60)
  const accepted: number[] = [] // cumulative accepted arrivals, at the end of each step
  const sent: number[] = [] // cumulative emails sent
  const depth: number[] = []
  let arrivedTotal = 0
  let sentTotal = 0
  let rejected = 0
  let queue = 0

  for (let i = 0; i < steps; i++) {
    const minute = (i * STEP) / 60
    let arriving = s.normalPerSec * STEP
    if (minute >= BATCH_START && minute < BATCH_START + s.batchMinutes) arriving += batchRate * STEP
    if (s.maxQueue > 0) {
      const room = Math.max(0, s.maxQueue - queue)
      rejected += Math.max(0, arriving - room)
      arriving = Math.min(arriving, room)
    }
    queue += arriving
    arrivedTotal += arriving
    const sending = Math.min(queue, capacityPerSec.value * STEP)
    queue -= sending
    sentTotal += sending
    accepted.push(arrivedTotal)
    sent.push(sentTotal)
    depth.push(queue)
  }

  // How long the message sent at step i had waited: find when it arrived, first in first out.
  const waits: number[] = []
  let j = 0
  for (let i = 0; i < steps; i++) {
    while (j < i && accepted[j] < sent[i]) j++
    waits.push((i - j) * STEP)
  }

  const peak = Math.max(...depth)
  const batchEnd = ((BATCH_START + s.batchMinutes) * 60) / STEP
  const drainedAt = depth.findIndex((d, i) => i >= batchEnd && d < 1)
  return {
    depth,
    waits,
    peak,
    worstWait: Math.max(...waits),
    drainMinutes: s.batch === 0 ? 0 : drainedAt < 0 ? null : ((drainedAt - batchEnd) * STEP) / 60,
    rejected,
  }
})

// Chart geometry
const W = 600
const H = 200
const PAD = { left: 52, right: 12, top: 12, bottom: 28 }
const plotW = W - PAD.left - PAD.right
const plotH = H - PAD.top - PAD.bottom
const yMax = computed(() => niceMax(Math.max(run.value.peak, s.maxQueue || 0, 10)))
const x = (i: number) => PAD.left + (i / (run.value.depth.length - 1)) * plotW
const y = (v: number) => PAD.top + plotH - (v / yMax.value) * plotH

function niceMax(v: number) {
  const step = 10 ** Math.floor(Math.log10(v))
  return Math.ceil(v / step) * step
}

const path = computed(() => {
  // Down-sample to one point per 30 s: plenty for 600 px.
  const d = run.value.depth
  const points: string[] = []
  for (let i = 0; i < d.length; i += 30) points.push(`${x(i).toFixed(1)},${y(d[i]).toFixed(1)}`)
  return 'M' + points.join('L')
})
const yTicks = computed(() => [0, 0.5, 1].map((f) => Math.round(f * yMax.value)))
const xTicks = [0, 30, 60, 90, 120]

const hover = ref<number | null>(null)
function onMove(event: PointerEvent) {
  const svg = event.currentTarget as SVGSVGElement
  const rect = svg.getBoundingClientRect()
  const px = ((event.clientX - rect.left) / rect.width) * W
  const i = Math.round(((px - PAD.left) / plotW) * (run.value.depth.length - 1))
  hover.value = i >= 0 && i < run.value.depth.length ? i : null
}

const fmtNumber = (n: number) => Math.round(n).toLocaleString('en-GB')
const fmtDuration = (seconds: number) =>
  seconds < 60 ? `${Math.round(seconds)} s` : seconds < 3600 ? `${Math.round(seconds / 60)} min` : `${(seconds / 3600).toFixed(1)} h`
const fmtClock = (i: number) => {
  const m = Math.floor((i * STEP) / 60)
  return `${m} min`
}
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Backlog simulator</span>
      <span class="note">Two hours of email work; payroll arrives at minute {{ BATCH_START }}</span>
    </div>

    <div class="presets" role="group" aria-label="Scenarios">
      <button v-for="(p, key) in presets" :key="key" type="button" :aria-pressed="preset === key" @click="applyPreset(key)">{{ p.label }}</button>
    </div>

    <div class="controls">
      <label :for="`${id}-batch`">Payroll emails <output>{{ fmtNumber(s.batch) }}</output></label>
      <input :id="`${id}-batch`" v-model.number="s.batch" type="range" min="0" max="200000" step="5000" @input="preset = ''" />

      <label :for="`${id}-workers`">Workers <output>{{ s.workers }}</output></label>
      <input :id="`${id}-workers`" v-model.number="s.workers" type="range" min="1" max="10" step="1" @input="preset = ''" />

      <label :for="`${id}-concurrency`">Emails in flight per worker <output>{{ s.concurrency }}</output></label>
      <input :id="`${id}-concurrency`" v-model.number="s.concurrency" type="range" min="1" max="50" step="1" @input="preset = ''" />

      <label :for="`${id}-limit`">Provider's limit (calls at once) <output>{{ s.providerLimit }}</output></label>
      <input :id="`${id}-limit`" v-model.number="s.providerLimit" type="range" min="5" max="100" step="5" @input="preset = ''" />

      <label :for="`${id}-maxqueue`">Maximum queue length <output>{{ s.maxQueue ? fmtNumber(s.maxQueue) : 'unlimited' }}</output></label>
      <input :id="`${id}-maxqueue`" v-model.number="s.maxQueue" type="range" min="0" max="50000" step="5000" @input="preset = ''" />
    </div>

    <p class="capacity">
      The workers can send <strong data-stat="capacity">{{ fmtNumber(capacityPerSec) }}</strong> emails a second;
      {{ fmtNumber(s.normalPerSec) }} a second arrive normally.
      <template v-if="refusedShare > 0">
        They try {{ s.workers * s.concurrency }} calls at once, but the provider takes {{ s.providerLimit }}:
        <strong>{{ Math.round(refusedShare * 100) }}%</strong> of attempts are refused and have to be retried.
      </template>
    </p>

    <figure class="chart">
      <figcaption>Emails waiting in the queue</figcaption>
      <svg
        :viewBox="`0 0 ${W} ${H}`"
        role="img"
        :aria-label="`Queue depth over two hours, peaking at ${fmtNumber(run.peak)} emails`"
        @pointermove="onMove"
        @pointerleave="hover = null"
      >
        <g class="grid">
          <line v-for="t in yTicks" :key="`g${t}`" :x1="PAD.left" :x2="W - PAD.right" :y1="y(t)" :y2="y(t)" />
        </g>
        <g class="axis">
          <text v-for="t in yTicks" :key="`y${t}`" :x="PAD.left - 6" :y="y(t) + 4" text-anchor="end">{{ fmtNumber(t) }}</text>
          <text v-for="t in xTicks" :key="`x${t}`" :x="x((t * 60) / STEP)" :y="H - 8" :text-anchor="t === xTicks[xTicks.length - 1] ? 'end' : 'middle'">{{ t }} min</text>
        </g>
        <line v-if="s.maxQueue" class="cap" :x1="PAD.left" :x2="W - PAD.right" :y1="y(s.maxQueue)" :y2="y(s.maxQueue)" />
        <path class="line" :d="path" />
        <g v-if="hover !== null">
          <line class="cross" :x1="x(hover)" :x2="x(hover)" :y1="PAD.top" :y2="PAD.top + plotH" />
          <circle class="dot" :cx="x(hover)" :cy="y(run.depth[hover])" r="4" />
        </g>
      </svg>
      <div v-if="hover !== null" class="tooltip" role="status">
        <strong>{{ fmtClock(hover) }}</strong>: {{ fmtNumber(run.depth[hover]) }} waiting;
        the email sent now waited {{ fmtDuration(run.waits[hover]) }}
      </div>
      <p v-if="s.maxQueue" class="meta">Dashed line: the maximum queue length.</p>
    </figure>

    <dl class="stats">
      <div><dt>Most emails waiting</dt><dd data-stat="peak">{{ fmtNumber(run.peak) }}</dd></div>
      <div><dt>Longest an email waited</dt><dd data-stat="wait">{{ fmtDuration(run.worstWait) }}</dd></div>
      <div>
        <dt>Queue empty again</dt>
        <dd data-stat="drain">{{ run.drainMinutes === null ? 'not within 2 h' : `${Math.round(run.drainMinutes)} min after payroll` }}</dd>
      </div>
      <div><dt>Rejected by a full queue</dt><dd data-stat="rejected" :class="{ bad: run.rejected > 0 }">{{ fmtNumber(run.rejected) }}</dd></div>
    </dl>
  </div>
</template>

<style scoped>
.simulator {
  --accent: #2a78d6;
  margin: 16px 0;
  padding: 12px 16px 16px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background-color: var(--vp-c-bg-soft);
}

:global(.dark) .simulator {
  --accent: #3987e5;
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

.capacity {
  margin: 12px 0 0;
  font-size: 14px;
}

.chart {
  position: relative;
  margin: 12px 0 0;
}

.chart svg {
  display: block;
  width: 100%;
  height: auto;
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

.line {
  fill: none;
  stroke: var(--accent);
  stroke-width: 2;
  stroke-linejoin: round;
}

.cap {
  stroke: var(--vp-c-text-2);
  stroke-width: 1;
  stroke-dasharray: 4 3;
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
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
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

button:focus-visible,
input:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
