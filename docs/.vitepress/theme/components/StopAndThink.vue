<script setup lang="ts">
// A stop-and-think question: the reader writes an answer before the model answer
// is revealed, then sees both side by side. Authored as a `::: think Question?`
// block (see config.mts); the block's body is the model answer.
//
// The reader's answer and revealed state are kept in localStorage so they survive
// a reload. Storage can be unavailable (private mode, blocked site data), so every
// access is wrapped and the component works without it.
import { computed, nextTick, onMounted, ref, useId, watch } from 'vue'

const props = defineProps<{ storageKey: string }>()

const id = useId()
const key = `handbook:think:${props.storageKey}`
const answer = ref('')
const revealed = ref(false)
const answerRegion = ref<HTMLElement>()

const canReveal = computed(() => answer.value.trim().length > 0)

function load() {
  try {
    const saved = JSON.parse(localStorage.getItem(key) ?? 'null')
    if (saved && typeof saved.answer === 'string') {
      answer.value = saved.answer
      revealed.value = saved.revealed === true
    }
  } catch {
    // Storage unavailable or corrupt: start fresh.
  }
}

function save() {
  try {
    localStorage.setItem(key, JSON.stringify({ answer: answer.value, revealed: revealed.value }))
  } catch {
    // Storage unavailable: the answer just won't persist.
  }
}

async function reveal() {
  revealed.value = true
  await nextTick()
  answerRegion.value?.focus()
}

function reset() {
  revealed.value = false
  answer.value = ''
}

onMounted(load)
watch([answer, revealed], save)
</script>

<template>
  <div class="stop-and-think custom-block">
    <p class="custom-block-title">Stop and think</p>
    <div class="question" :id="`${id}-q`"><slot name="question" /></div>

    <template v-if="!revealed">
      <label class="label" :for="`${id}-answer`">Your answer, before you look</label>
      <textarea
        :id="`${id}-answer`"
        v-model="answer"
        rows="3"
        :aria-describedby="`${id}-q`"
        placeholder="A sentence or two is enough. Committing to an answer is the point."
      />
      <div class="actions">
        <button type="button" class="reveal" :disabled="!canReveal" @click="reveal">Compare with the answer</button>
        <button type="button" class="skip" @click="reveal">Skip and show the answer</button>
      </div>
    </template>

    <div v-show="revealed" ref="answerRegion" class="revealed" tabindex="-1" :aria-labelledby="`${id}-model`">
      <div v-if="answer.trim()" class="yours">
        <p class="heading">Your answer</p>
        <p class="your-text">{{ answer }}</p>
      </div>
      <p class="heading" :id="`${id}-model`">Model answer</p>
      <div class="model"><slot /></div>
      <button type="button" class="skip" @click="reset">Try again</button>
    </div>
  </div>
</template>

<style scoped>
.stop-and-think {
  border-color: var(--vp-c-brand-2);
  background-color: var(--vp-c-brand-soft);
  color: var(--vp-c-text-1);
}

.question :deep(p) {
  margin: 8px 0;
  font-weight: 500;
}

.label,
.heading {
  display: block;
  margin: 12px 0 4px;
  font-size: 13px;
  font-weight: 600;
  color: var(--vp-c-text-2);
}

textarea {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background-color: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font: inherit;
  font-size: 14px;
  line-height: 1.5;
  resize: vertical;
}

textarea:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 1px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 16px;
  margin-top: 8px;
}

.reveal {
  padding: 6px 14px;
  border-radius: 6px;
  background-color: var(--vp-c-brand-3);
  color: var(--vp-c-white);
  font-size: 14px;
  font-weight: 500;
}

.reveal:hover:not(:disabled) {
  background-color: var(--vp-c-brand-2);
}

.reveal:disabled {
  background-color: var(--vp-c-default-soft);
  color: var(--vp-c-text-3);
  cursor: not-allowed;
}

.skip {
  font-size: 13px;
  color: var(--vp-c-text-2);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.skip:hover {
  color: var(--vp-c-brand-1);
}

.reveal:focus-visible,
.skip:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}

.revealed:focus {
  outline: none;
}

.yours {
  padding: 4px 12px;
  border-left: 3px solid var(--vp-c-divider);
}

.your-text {
  margin: 4px 0;
  white-space: pre-wrap;
}

.model :deep(> :first-child) {
  margin-top: 4px;
}
</style>
