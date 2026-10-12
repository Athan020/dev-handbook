<script setup lang="ts">
// Sharding simulator for B1 stage 7.
//
// 2,000 accounts are spread over shards, either by hash(key) mod N or with a
// consistent-hash ring. Adding or removing a shard highlights the accounts that moved.
// The bars show each shard's load against a fair share, with equal load per account or
// with the lab's skew (a few very busy accounts). Everything is computed, not animated:
// the same settings always give the same picture.
import { computed, ref, useId } from 'vue'

type Method = 'modulo' | 'ring'
const KEYS = 2000
const keys = Array.from({ length: KEYS }, (_, i) => `account:${i + 1}`)

// FNV-1a: a small, stable 32-bit string hash (the lab code uses XxHash3; any stable hash works).
function hash(text: string): number {
  let h = 0x811c9dc5
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i)
    h = Math.imul(h, 0x01000193)
  }
  // Finalise to spread nearby inputs (avalanche), then make unsigned.
  h ^= h >>> 16
  h = Math.imul(h, 0x85ebca6b)
  h ^= h >>> 13
  return h >>> 0
}
const keyHashes = keys.map(hash)

// Load per account, the shape of the lab data: account i gets i^(1/3) - (i-1)^(1/3) of it.
const skewedLoad = keys.map((_, i) => Math.cbrt(i + 1) - Math.cbrt(i))

const id = useId()
const method = ref<Method>('ring')
const shards = ref(4)
const virtualNodes = ref(64)
const skewed = ref(false)
const previous = ref<number[] | null>(null)
const lastChange = ref('')

function assign(m: Method, n: number, vnodes: number): number[] {
  if (m === 'modulo') return keyHashes.map((h) => h % n)
  const points: { at: number; shard: number }[] = []
  for (let s = 0; s < n; s++) for (let v = 0; v < vnodes; v++) points.push({ at: hash(`shard-${s}#${v}`), shard: s })
  points.sort((a, b) => a.at - b.at)
  return keyHashes.map((h) => {
    // First point at or after the key's hash, wrapping around.
    let lo = 0
    let hi = points.length
    while (lo < hi) {
      const mid = (lo + hi) >> 1
      if (points[mid].at < h) lo = mid + 1
      else hi = mid
    }
    return points[lo === points.length ? 0 : lo].shard
  })
}

const owners = computed(() => assign(method.value, shards.value, virtualNodes.value))

const moved = computed(() => {
  if (!previous.value) return null
  return owners.value.map((o, i) => o !== previous.value![i])
})
const movedShare = computed(() =>
  moved.value ? Math.round((1000 * moved.value.filter(Boolean).length) / KEYS) / 10 : null,
)

function change(delta: number) {
  const next = shards.value + delta
  if (next < 1 || next > 12) return
  previous.value = owners.value
  const from = shards.value
  shards.value = next
  const least = delta > 0 ? (100 * delta) / next : (100 * -delta) / from
  lastChange.value = `${from} to ${next} shards (the least possible: ${least.toFixed(1)}%)`
}

function settingsChanged() {
  previous.value = null
  lastChange.value = ''
}

const loads = computed(() => {
  const totals = Array.from({ length: shards.value }, () => 0)
  owners.value.forEach((o, i) => (totals[o] += skewed.value ? skewedLoad[i] : 1))
  const sum = totals.reduce((a, b) => a + b, 0)
  const fair = sum / shards.value
  return totals.map((t, shard) => ({ shard, ratio: t / fair }))
})
const maxRatio = computed(() => Math.max(...loads.value.map((l) => l.ratio)))
const minRatio = computed(() => Math.min(...loads.value.map((l) => l.ratio)))
const scale = computed(() => Math.max(2, Math.ceil(maxRatio.value * 2) / 2))

// Ring geometry: positions around a circle by hash.
const R = 100
const toXY = (h: number, r: number) => {
  const a = (h / 2 ** 32) * 2 * Math.PI - Math.PI / 2
  return { x: 120 + r * Math.cos(a), y: 120 + r * Math.sin(a) }
}
const ringPoints = computed(() => {
  if (method.value !== 'ring') return []
  const out: { x1: number; y1: number; x2: number; y2: number }[] = []
  for (let s = 0; s < shards.value; s++) {
    for (let v = 0; v < Math.min(virtualNodes.value, 64); v++) {
      const h = hash(`shard-${s}#${v}`)
      const a = toXY(h, R - 6)
      const b = toXY(h, R + 6)
      out.push({ x1: a.x, y1: a.y, x2: b.x, y2: b.y })
    }
  }
  return out
})
// Draw a sample of keys on the ring, so it stays readable.
const sample = keyHashes.map((h, i) => ({ h, i })).filter((_, i) => i % 5 === 0)
const keyDots = computed(() =>
  sample.map(({ h, i }) => ({ ...toXY(h, R), moved: moved.value?.[i] ?? false })),
)
</script>

<template>
  <div class="simulator">
    <div class="header">
      <span class="title">Sharding simulator</span>
      <span class="note">{{ KEYS.toLocaleString('en-GB') }} accounts</span>
    </div>

    <div class="controls">
      <label :for="`${id}-method`">Choose a shard by</label>
      <select :id="`${id}-method`" v-model="method" @change="settingsChanged">
        <option value="modulo">hash(key) mod N</option>
        <option value="ring">A consistent-hash ring</option>
      </select>

      <label :for="`${id}-vnodes`">Virtual nodes per shard <output>{{ virtualNodes }}</output></label>
      <input :id="`${id}-vnodes`" v-model.number="virtualNodes" type="range" min="1" max="200" step="1" :disabled="method !== 'ring'" @input="settingsChanged" />

      <label :for="`${id}-skew`">Load per account</label>
      <span><input :id="`${id}-skew`" v-model="skewed" type="checkbox" /> realistic: a few very busy accounts</span>
    </div>

    <div class="actions">
      <span class="count" data-stat="shards">{{ shards }} shard{{ shards === 1 ? '' : 's' }}</span>
      <button type="button" :disabled="shards >= 12" @click="change(1)">Add a shard</button>
      <button type="button" :disabled="shards <= 1" @click="change(-1)">Remove a shard</button>
    </div>

    <p class="moved" aria-live="polite">
      <template v-if="movedShare !== null">
        <strong data-stat="moved">{{ movedShare }}%</strong> of accounts changed shard, going from {{ lastChange }}.
      </template>
      <template v-else>Add or remove a shard to see which accounts move.</template>
    </p>

    <div class="panels">
      <figure class="ring">
        <svg viewBox="0 0 240 240" role="img" :aria-label="`Accounts placed by hash; ${movedShare ?? 0}% moved in the last change`">
          <circle cx="120" cy="120" :r="R" class="track" />
          <line v-for="(p, i) in ringPoints" :key="`p${i}`" :x1="p.x1" :y1="p.y1" :x2="p.x2" :y2="p.y2" class="point" />
          <circle v-for="(d, i) in keyDots" :key="`k${i}`" :cx="d.x" :cy="d.y" :r="d.moved ? 3.2 : 1.8" :class="d.moved ? 'key moved' : 'key'" />
        </svg>
        <figcaption>
          <span class="swatch moved" /> moved in the last change <span class="swatch" /> stayed
          <template v-if="method === 'ring'"> · ticks: shard points on the ring</template>
        </figcaption>
      </figure>

      <figure class="bars">
        <figcaption class="bars-title">Load per shard, as a multiple of a fair share</figcaption>
        <div v-for="l in loads" :key="l.shard" class="bar-row" :title="`shard-${l.shard}: ${l.ratio.toFixed(2)}× a fair share`">
          <span class="bar-label">shard-{{ l.shard }}</span>
          <span class="bar-track">
            <span class="bar" :style="{ width: `${(l.ratio / scale) * 100}%` }" />
            <span class="fair" :style="{ left: `${(1 / scale) * 100}%` }" />
          </span>
          <span class="bar-value">{{ l.ratio.toFixed(2) }}×</span>
        </div>
        <p class="meta">
          Busiest <strong data-stat="max">{{ maxRatio.toFixed(2) }}×</strong>, quietest
          <strong data-stat="min">{{ minRatio.toFixed(2) }}×</strong>. The thin line marks a fair share.
        </p>
      </figure>
    </div>
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
  font-size: 14px;
  font-weight: 600;
}

.note,
.meta,
figcaption {
  font-size: 13px;
  color: var(--vp-c-text-2);
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

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  margin-top: 12px;
}

.count {
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.actions button {
  padding: 4px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-c-bg);
  font-size: 14px;
}

.actions button:disabled {
  color: var(--vp-c-text-3);
  cursor: not-allowed;
}

.moved {
  margin: 10px 0 0;
  font-size: 14px;
}

.panels {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr);
  gap: 16px;
  align-items: start;
  margin-top: 8px;
}

@media (max-width: 620px) {
  .panels {
    grid-template-columns: 1fr;
  }
}

figure {
  margin: 0;
}

.ring svg {
  display: block;
  width: 100%;
  max-width: 260px;
  margin: 0 auto;
}

.track {
  fill: none;
  stroke: var(--vp-c-divider);
  stroke-width: 2;
}

.point {
  stroke: var(--vp-c-text-3);
  stroke-width: 1;
}

.key {
  fill: var(--context);
}

.key.moved {
  fill: var(--accent);
}

.swatch {
  display: inline-block;
  width: 8px;
  height: 8px;
  margin: 0 2px 0 6px;
  border-radius: 50%;
  background-color: var(--context);
  vertical-align: middle;
}

.swatch.moved {
  width: 10px;
  height: 10px;
  margin-left: 0;
  background-color: var(--accent);
}

.bars-title {
  margin-bottom: 6px;
}

.bar-row {
  display: grid;
  grid-template-columns: 62px minmax(0, 1fr) 46px;
  gap: 8px;
  align-items: center;
  margin: 3px 0;
  font-size: 12.5px;
  font-variant-numeric: tabular-nums;
}

.bar-label {
  color: var(--vp-c-text-2);
}

.bar-track {
  position: relative;
  height: 12px;
}

.bar {
  position: absolute;
  inset: 0 auto 0 0;
  border-radius: 0 4px 4px 0;
  background-color: var(--accent);
}

.fair {
  position: absolute;
  top: -2px;
  bottom: -2px;
  width: 1px;
  background-color: var(--vp-c-text-1);
}

.bar-value {
  text-align: right;
}

.meta {
  margin: 8px 0 0;
}

button:focus-visible,
input:focus-visible,
select:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
