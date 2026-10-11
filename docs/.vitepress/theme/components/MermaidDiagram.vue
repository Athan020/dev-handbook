<script setup lang="ts">
// Renders one ```mermaid block. The markdown fence is turned into
// <MermaidDiagram code="..."> in config.mts; the code arrives URI-encoded.
//
// Mermaid is imported on demand, so pages without diagrams never load it.
// The diagram is redrawn when the reader switches between light and dark.
import { onMounted, ref, watch } from 'vue'
import { useData } from 'vitepress'

const props = defineProps<{ code: string }>()
const { isDark } = useData()

const svg = ref('')
const error = ref('')

let nextId = 0
// mermaid.render uses shared global state, so diagrams render one at a time.
let queue: Promise<unknown> = Promise.resolve()

async function draw() {
  const source = decodeURIComponent(props.code)
  const { default: mermaid } = await import('mermaid')
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: 'strict',
    theme: isDark.value ? 'dark' : 'default',
  })
  try {
    const { svg: rendered } = await mermaid.render(`mermaid-diagram-${nextId++}`, source)
    svg.value = rendered
    error.value = ''
  } catch (e) {
    svg.value = ''
    error.value = e instanceof Error ? e.message : String(e)
    console.error('Mermaid diagram failed to render:', e)
  }
}

const render = () => (queue = queue.then(draw, draw))

onMounted(render)
watch(isDark, render)
</script>

<template>
  <div class="mermaid">
    <div v-if="svg" v-html="svg" />
    <div v-else-if="error" class="mermaid-error" role="alert">
      <p><strong>This diagram failed to render.</strong></p>
      <pre>{{ error }}</pre>
    </div>
  </div>
</template>
