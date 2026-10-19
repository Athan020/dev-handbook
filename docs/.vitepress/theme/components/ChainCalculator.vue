<script setup lang="ts">
// Chain calculator for B5 stage 4: what a chain of synchronous calls does to a request.
//
// A request needs an answer from each of n services. Each one answers with probability a
// (independently; real outages often aren't, which is why the page's lab takes one service
// down for a while instead). A retry asks a failed service once more: 1 - (1 - a)^2.
// Each call takes about `typical` ms, but `slowShare` of calls take `slowMs` instead.
//   In sequence, the typical times add up; in parallel, the request waits for the slowest.
//   Either way, a request is slow if any one of its calls is: 1 - (1 - slowShare)^n.
import { computed, reactive, useId } from 'vue'

const id = useId()
const s = reactive({ services: 4, availability: 99.9, retry: false, parallel: false })
const TYPICAL_MS = 25
const SLOW_SHARE = 0.01
const SLOW_MS = 250
const MINUTES_PER_30_DAYS = 30 * 24 * 60

const availabilities = [99, 99.9, 99.95, 99.99]

const perCall = computed(() => {
  const a = s.availability / 100
  return s.retry ? 1 - (1 - a) ** 2 : a
})

function row(n: number) {
  const available = perCall.value ** n
  return {
    n,
    available,
    downMinutes: (1 - available) * MINUTES_PER_30_DAYS,
    typicalMs: s.parallel ? TYPICAL_MS : n * TYPICAL_MS,
    slowShare: 1 - (1 - SLOW_SHARE) ** n,
  }
}

const rows = computed(() => Array.from({ length: 8 }, (_, i) => row(i + 1)))
const current = computed(() => row(s.services))
const single = computed(() => row(1))

// Truncated, not rounded, so 99.99996% never shows as 100%.
const percent = (x: number) => {
  const p = x * 100
  const digits = p >= 99.999 ? 4 : p >= 99.99 ? 3 : p >= 99 ? 2 : 1
  const scale = 10 ** digits
  return `${(Math.floor(p * scale) / scale).toFixed(digits)}%`
}
const minutes = (m: number) => (m >= 120 ? `${(m / 60).toFixed(1)} h` : m >= 1 ? `${Math.round(m)} min` : `${Math.round(m * 60)} s`)
const oneIn = (share: number) => `1 in ${Math.max(1, Math.round(1 / share))}`
</script>

<template>
  <div class="simulator chain-calculator">
    <div class="header">
      <span class="title">Chain calculator</span>
      <span class="note">A request that needs every service in the chain</span>
    </div>

    <div class="controls">
      <label :for="`${id}-services`">Services in the chain <output>{{ s.services }}</output></label>
      <input :id="`${id}-services`" v-model.number="s.services" type="range" min="1" max="8" step="1" />
    </div>

    <fieldset class="choices">
      <legend>Each service is available</legend>
      <button
        v-for="a in availabilities" :key="a" type="button"
        :aria-pressed="s.availability === a" :data-availability="a"
        @click="s.availability = a"
      >{{ a }}%</button>
    </fieldset>

    <fieldset class="choices">
      <legend>Calls</legend>
      <button type="button" :aria-pressed="!s.parallel" data-mode="sequence" @click="s.parallel = false">In sequence</button>
      <button type="button" :aria-pressed="s.parallel" data-mode="parallel" @click="s.parallel = true">In parallel</button>
      <button type="button" :aria-pressed="s.retry" data-mode="retry" @click="s.retry = !s.retry">Retry a failure once</button>
    </fieldset>

    <div class="stats">
      <div class="stat">
        <span class="label">Requests that succeed</span>
        <span class="value" data-stat="available">{{ percent(current.available) }}</span>
        <span class="sub">one service alone: {{ percent(single.available) }}</span>
      </div>
      <div class="stat">
        <span class="label">Down, per 30 days</span>
        <span class="value" data-stat="down">{{ minutes(current.downMinutes) }}</span>
        <span class="sub">one service alone: {{ minutes(single.downMinutes) }}</span>
      </div>
      <div class="stat">
        <span class="label">Typical time</span>
        <span class="value" data-stat="typical">{{ current.typicalMs }} ms</span>
        <span class="sub">{{ s.parallel ? 'the slowest call' : 'every call, added up' }}</span>
      </div>
      <div class="stat">
        <span class="label">Requests that meet a slow call</span>
        <span class="value" data-stat="slow">{{ oneIn(current.slowShare) }}</span>
        <span class="sub">one service alone: {{ oneIn(single.slowShare) }}</span>
      </div>
    </div>

    <div class="table-wrap">
    <table class="results">
      <caption>Each call takes about {{ TYPICAL_MS }} ms, and 1 in {{ 1 / SLOW_SHARE }} takes {{ SLOW_MS }} ms. Failures are assumed independent.</caption>
      <thead>
        <tr>
          <th scope="col">Services</th>
          <th scope="col">Succeed</th>
          <th scope="col">Down per 30 days</th>
          <th scope="col">Typical</th>
          <th scope="col">Slow</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in rows" :key="r.n" :class="{ current: r.n === s.services }" :data-services="r.n">
          <th scope="row">{{ r.n }}</th>
          <td>{{ percent(r.available) }}</td>
          <td>{{ minutes(r.downMinutes) }}</td>
          <td>{{ r.typicalMs }} ms</td>
          <td>{{ oneIn(r.slowShare) }}</td>
        </tr>
      </tbody>
    </table>
    </div>
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
.sub {
  font-size: 13px;
  color: var(--vp-c-text-2);
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

.choices {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 10px 0 0;
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

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.stat {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 10px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-c-bg);
}

.stat .label {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.stat .value {
  font-size: 20px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.results {
  display: table;
  width: 100%;
  margin: 12px 0 0;
  border-collapse: collapse;
  font-size: 14px;
}

.results caption {
  caption-side: bottom;
  padding-top: 6px;
  font-size: 12px;
  color: var(--vp-c-text-2);
  text-align: left;
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

.table-wrap {
  overflow-x: auto;
}

.results td {
  white-space: nowrap;
}

@media (max-width: 520px) {
  .results th,
  .results td {
    padding: 5px 6px;
    font-size: 13px;
  }
}

.results tr.current td,
.results tr.current th {
  background-color: var(--vp-c-brand-soft);
  font-weight: 600;
}

button:focus-visible,
input:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
