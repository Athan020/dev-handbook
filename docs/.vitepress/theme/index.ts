import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import MermaidDiagram from './components/MermaidDiagram.vue'
import StopAndThink from './components/StopAndThink.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('MermaidDiagram', MermaidDiagram)
    app.component('StopAndThink', StopAndThink)
  },
} satisfies Theme
