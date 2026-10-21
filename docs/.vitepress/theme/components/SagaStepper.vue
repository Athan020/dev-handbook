<script setup lang="ts">
// Saga stepper for B5 stage 5: a R100 card top-up, from an account holding R500 onto an empty
// card, run three ways, with one thing going wrong. It shows each step, and what is left on
// both sides afterwards. The rules match the lab's three modes (Lab:TopUps).
import { computed, reactive } from 'vue'

type Mode = 'naive' | 'memory' | 'saga'
type Fault = 'none' | 'declined' | 'noanswer' | 'lostreply' | 'crash'
type Actor = 'Ledger' | 'Cards' | 'Customer' | 'Orchestrator' | 'Process'

const modes: { key: Mode; label: string }[] = [
  { key: 'naive', label: 'Naive' },
  { key: 'memory', label: 'In-memory compensation' },
  { key: 'saga', label: 'Saga with saved state' },
]

const faults: { key: Fault; label: string; describe: string }[] = [
  { key: 'none', label: 'Nothing', describe: 'Every step works.' },
  { key: 'declined', label: 'Card declined', describe: 'The cards service refuses: the card is blocked.' },
  { key: 'noanswer', label: 'No answer', describe: 'The cards service is down for a moment, and credits nothing.' },
  { key: 'lostreply', label: 'Lost reply', describe: 'The cards service credits the card, but its reply never arrives.' },
  { key: 'crash', label: 'Crash after the debit', describe: 'The process dies right after taking the money.' },
]

const s = reactive({ mode: 'saga' as Mode, fault: 'lostreply' as Fault })

interface Step { actor: Actor; text: string; bad?: boolean }

const run = computed(() => {
  const steps: Step[] = []
  let account = 500
  let card = 0
  let transit = 0
  let customerTold = ''

  const debit = () => {
    account -= 100
    transit += 100
    steps.push({ actor: 'Ledger', text: s.mode === 'saga'
      ? 'Takes R100 from the account into "in transit", and saves the top-up as Debited, in one transaction.'
      : 'Takes R100 from the account into "in transit".' })
  }
  const complete = () => {
    transit -= 100
    steps.push({ actor: 'Ledger', text: 'Moves the R100 out of "in transit": the top-up is complete.' })
  }
  const refund = (why: string, bad = false) => {
    account += 100
    transit -= 100
    steps.push({ actor: 'Ledger', text: `Refunds the R100: ${why}`, bad })
  }

  debit()

  if (s.fault === 'crash' && s.mode !== 'saga') {
    steps.push({ actor: 'Process', text: 'Dies. The plan for what to do next was only in memory.', bad: true })
    customerTold = 'an error.'
  } else if (s.mode === 'saga') {
    customerTold = '"Your top-up is on its way."'
    if (s.fault === 'crash') {
      steps.push({ actor: 'Process', text: 'Dies, after saving the top-up as Debited.' })
      steps.push({ actor: 'Orchestrator', text: 'After the restart, finds the Debited top-up and carries on.' })
    }
    if (s.fault === 'declined') {
      steps.push({ actor: 'Cards', text: 'Declines: the card is blocked. A definite no.' })
      refund('the compensating step, saved with the money as Refunded.')
    } else {
      if (s.fault === 'noanswer') {
        steps.push({ actor: 'Cards', text: 'Doesn’t answer. Nothing was credited, but the ledger can’t know that.' })
        steps.push({ actor: 'Orchestrator', text: 'Tries again later, with the same reference.' })
      }
      if (s.fault === 'lostreply') {
        card += 100
        steps.push({ actor: 'Cards', text: 'Credits R100 to the card. The reply is lost.' })
        steps.push({ actor: 'Orchestrator', text: 'No answer, so tries again later, with the same reference.' })
        steps.push({ actor: 'Cards', text: 'Recognises the reference: already credited. Answers "done", and credits nothing more.' })
      } else {
        card += 100
        steps.push({ actor: 'Cards', text: 'Credits R100 to the card.' })
      }
      complete()
    }
  } else {
    // Naive and in-memory: the request does every step while the customer waits.
    if (s.fault === 'none') {
      card += 100
      steps.push({ actor: 'Cards', text: 'Credits R100 to the card.' })
      complete()
      customerTold = '"Done."'
    } else if (s.fault === 'declined') {
      steps.push({ actor: 'Cards', text: 'Declines: the card is blocked.' })
      if (s.mode === 'memory') refund('the compensation, run from memory.')
      else steps.push({ actor: 'Ledger', text: 'Nothing undoes the debit.', bad: true })
      customerTold = '"Your card was declined."'
    } else if (s.fault === 'noanswer') {
      steps.push({ actor: 'Cards', text: 'Doesn’t answer. Nothing was credited.' })
      if (s.mode === 'memory') refund('no answer, so it assumes the card wasn’t credited. This time it’s right.')
      else steps.push({ actor: 'Ledger', text: 'Nothing undoes the debit.', bad: true })
      customerTold = '"The top-up failed."'
    } else if (s.fault === 'lostreply') {
      card += 100
      steps.push({ actor: 'Cards', text: 'Credits R100 to the card. The reply is lost.' })
      if (s.mode === 'memory') refund('no answer, so it assumes the card wasn’t credited. It was.', true)
      else steps.push({ actor: 'Ledger', text: 'Gives up. The R100 stays "in transit", though the card has it.', bad: true })
      customerTold = '"The top-up failed."'
    }
  }
  steps.push({ actor: 'Customer', text: `Hears ${customerTold}` })

  // What's left, and whether it's right.
  let verdict: string
  let ok = false
  if (card === 100 && account === 400 && transit === 0) { verdict = 'Right: the money is on the card, and the ledger agrees.'; ok = true }
  else if (card === 0 && account === 500 && transit === 0) { verdict = 'Right: the top-up didn’t happen, and the money is back.'; ok = true }
  else if (card === 100 && account === 500) verdict = 'Wrong: the card was credited and the money refunded. R100 created from nothing.'
  else if (card === 100 && transit === 100) verdict = 'Wrong: the card has the money, but the ledger still has it in transit, and the customer was told it failed.'
  else verdict = 'Wrong: R100 taken from the customer, and on no card. It sits in transit until someone notices.'

  return { steps, account, card, transit, verdict, ok }
})

const current = computed(() => faults.find((f) => f.key === s.fault)!)
</script>

<template>
  <div class="simulator saga-stepper">
    <div class="header">
      <span class="title">Saga stepper</span>
      <span class="note">A R100 card top-up, from an account holding R500</span>
    </div>

    <fieldset class="choices">
      <legend>How it's run</legend>
      <button
        v-for="m in modes" :key="m.key" type="button"
        :aria-pressed="s.mode === m.key" :data-mode="m.key"
        @click="s.mode = m.key"
      >{{ m.label }}</button>
    </fieldset>

    <fieldset class="choices">
      <legend>What goes wrong</legend>
      <button
        v-for="f in faults" :key="f.key" type="button"
        :aria-pressed="s.fault === f.key" :data-fault="f.key"
        @click="s.fault = f.key"
      >{{ f.label }}</button>
    </fieldset>
    <p class="describe">{{ current.describe }}</p>

    <ol class="steps">
      <li v-for="(step, i) in run.steps" :key="i" :class="{ bad: step.bad }">
        <span class="actor">{{ step.actor }}</span>
        <span class="text">{{ step.text }}</span>
      </li>
    </ol>

    <table class="results">
      <thead>
        <tr><th scope="col">Afterwards</th><th scope="col">Customer's account</th><th scope="col">In transit</th><th scope="col">Card</th></tr>
      </thead>
      <tbody>
        <tr>
          <th scope="row">Balance</th>
          <td data-col="account">R{{ run.account }}</td>
          <td data-col="transit">R{{ run.transit }}</td>
          <td data-col="card">R{{ run.card }}</td>
        </tr>
      </tbody>
    </table>
    <p class="verdict" :class="run.ok ? 'ok' : 'wrong'" data-verdict>{{ run.verdict }}</p>
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
.describe {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.describe {
  margin: 6px 0 0;
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

.steps {
  margin: 12px 0 0;
  padding: 0;
  list-style: none;
  counter-reset: step;
}

.steps li {
  display: grid;
  grid-template-columns: 7.5em 1fr;
  gap: 8px;
  margin: 0;
  padding: 6px 0;
  border-top: 1px solid var(--vp-c-divider);
}

.steps li.bad .text {
  color: var(--vp-c-danger-1);
  font-weight: 500;
}

.actor {
  font-size: 13px;
  font-weight: 600;
  color: var(--vp-c-text-2);
}

@media (max-width: 520px) {
  .steps li {
    grid-template-columns: 1fr;
    gap: 2px;
  }
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

.verdict {
  margin: 10px 0 0;
  padding: 8px 10px;
  border-radius: 6px;
  font-weight: 500;
}

.verdict.ok {
  background-color: var(--vp-c-tip-soft);
  color: var(--vp-c-tip-1);
}

.verdict.wrong {
  background-color: var(--vp-c-danger-soft);
  color: var(--vp-c-danger-1);
}

button:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}
</style>
