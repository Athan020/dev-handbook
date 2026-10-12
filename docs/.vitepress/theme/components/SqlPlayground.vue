<script setup lang="ts">
// An editable SQL block that runs against real Postgres in the browser (PGlite).
// Authored as a ```sql playground fence (see config.mts); the fence body is the starting
// query. The database starts on the first run, so readers who skip it download nothing.
import { computed, ref, useId } from 'vue'
import { useData } from 'vitepress'
import type { Results } from '@electric-sql/pglite'
import { DATASET, resetDatabase, run } from '../playground/database'

const props = defineProps<{ code: string }>()
const { page } = useData()

const MAX_ROWS = 50
const id = useId()
const initial = decodeURIComponent(props.code).trimEnd()
const sql = ref(initial)
const state = ref<'idle' | 'starting' | 'running' | 'done' | 'error'>('idle')
const started = ref(false)
const results = ref<Results<unknown[]>[]>([])
const error = ref('')
const elapsed = ref(0)

const busy = computed(() => state.value === 'starting' || state.value === 'running')
const dataset = `${DATASET.accounts.toLocaleString('en-GB')} accounts and ${DATASET.transactions.toLocaleString('en-GB')} transactions`

async function execute() {
  if (busy.value) return
  error.value = ''
  state.value = started.value ? 'running' : 'starting'
  const pageKey = page.value.relativePath
  try {
    // Time only the query, not the first-run database start.
    if (!started.value) await run(pageKey, 'SELECT 1')
    started.value = true
    state.value = 'running'
    const t0 = performance.now()
    results.value = await run(pageKey, sql.value)
    elapsed.value = performance.now() - t0
    state.value = 'done'
  } catch (e) {
    results.value = []
    error.value = e instanceof Error ? e.message : String(e)
    state.value = 'error'
  }
}

function reset() {
  resetDatabase(page.value.relativePath)
  started.value = false
  results.value = []
  error.value = ''
  state.value = 'idle'
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
    event.preventDefault()
    execute()
  }
}

const isPlan = (r: Results<unknown[]>) => r.fields.length === 1 && r.fields[0].name === 'QUERY PLAN'

function format(value: unknown): string {
  if (value === null || value === undefined) return 'NULL'
  if (value instanceof Date) return value.toISOString()
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}
</script>

<template>
  <div class="sql-playground">
    <div class="header">
      <span class="title">SQL playground</span>
      <span class="note">Real Postgres in your browser, with {{ dataset }}</span>
    </div>

    <label class="visually-hidden" :for="`${id}-sql`">SQL to run</label>
    <textarea
      :id="`${id}-sql`"
      v-model="sql"
      class="editor"
      spellcheck="false"
      autocapitalize="off"
      :rows="Math.min(Math.max(sql.split('\n').length, 3), 16)"
      @keydown="onKeydown"
    />

    <div class="actions">
      <button type="button" class="run" :disabled="busy" @click="execute">
        {{ state === 'starting' ? 'Starting database…' : state === 'running' ? 'Running…' : 'Run' }}
      </button>
      <span class="hint">Ctrl+Enter or ⌘+Enter</span>
      <button v-if="sql !== initial" type="button" class="link" @click="sql = initial">Restore query</button>
      <button v-if="started" type="button" class="link" @click="reset">Reset database</button>
    </div>

    <p v-if="state === 'starting'" class="status" role="status">
      Starting Postgres and generating the data. This takes a few seconds the first time on each page.
    </p>

    <div v-if="state === 'error'" class="error" role="alert">
      <pre>{{ error }}</pre>
    </div>

    <div v-if="state === 'done'" class="results" role="status">
      <template v-for="(result, i) in results" :key="i">
        <pre v-if="isPlan(result)" class="plan">{{ result.rows.map((r) => r[0]).join('\n') }}</pre>
        <div v-else-if="result.fields.length" class="table-wrap">
          <table>
            <thead>
              <tr><th v-for="field in result.fields" :key="field.name">{{ field.name }}</th></tr>
            </thead>
            <tbody>
              <tr v-for="(row, r) in result.rows.slice(0, MAX_ROWS)" :key="r">
                <td v-for="(value, c) in row" :key="c">{{ format(value) }}</td>
              </tr>
            </tbody>
          </table>
          <p class="meta">
            {{ result.rows.length.toLocaleString('en-GB') }} row{{ result.rows.length === 1 ? '' : 's' }}<template
              v-if="result.rows.length > MAX_ROWS">, showing the first {{ MAX_ROWS }}</template>
          </p>
        </div>
        <p v-else class="meta">Done{{ result.affectedRows ? `: ${result.affectedRows.toLocaleString('en-GB')} rows affected` : '' }}</p>
      </template>
      <p class="meta">Ran in {{ elapsed < 10 ? elapsed.toFixed(1) : Math.round(elapsed) }} ms</p>
    </div>
  </div>
</template>

<style scoped>
.sql-playground {
  margin: 16px 0;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background-color: var(--vp-c-bg-soft);
  padding: 12px 16px 16px;
}

.header {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 12px;
  margin-bottom: 8px;
}

.title {
  font-size: 14px;
  font-weight: 600;
}

.note,
.hint,
.meta {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.editor {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-code-block-bg);
  color: var(--vp-c-text-1);
  font-family: var(--vp-font-family-mono);
  font-size: 13px;
  line-height: 1.6;
  resize: vertical;
  tab-size: 4;
}

.editor:focus-visible,
button:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 16px;
  margin-top: 8px;
}

.run {
  padding: 6px 16px;
  border-radius: 6px;
  background-color: var(--vp-c-brand-3);
  color: var(--vp-c-white);
  font-size: 14px;
  font-weight: 500;
}

.run:hover:not(:disabled) {
  background-color: var(--vp-c-brand-2);
}

.run:disabled {
  opacity: 0.7;
  cursor: progress;
}

.link {
  font-size: 13px;
  color: var(--vp-c-text-2);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.link:hover {
  color: var(--vp-c-brand-1);
}

.status {
  margin: 12px 0 0;
  font-size: 14px;
  color: var(--vp-c-text-2);
}

.error pre,
.plan {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: 6px;
  overflow-x: auto;
  font-family: var(--vp-font-family-mono);
  font-size: 12.5px;
  line-height: 1.6;
  white-space: pre;
}

.error pre {
  background-color: var(--vp-c-danger-soft);
  color: var(--vp-c-text-1);
  white-space: pre-wrap;
}

.plan {
  background-color: var(--vp-code-block-bg);
  color: var(--vp-c-text-1);
}

.table-wrap {
  margin-top: 12px;
  overflow-x: auto;
}

/* Undo .vp-doc table styles that don't suit dense result grids. */
.table-wrap table {
  display: table;
  margin: 0;
  border-collapse: collapse;
  font-family: var(--vp-font-family-mono);
  font-size: 12.5px;
}

.table-wrap th,
.table-wrap td {
  padding: 4px 10px;
  border: 1px solid var(--vp-c-divider);
  white-space: nowrap;
  text-align: left;
}

.meta {
  margin: 6px 0 0;
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}
</style>
