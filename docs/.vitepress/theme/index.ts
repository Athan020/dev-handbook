import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import CacheSimulator from './components/CacheSimulator.vue'
import MermaidDiagram from './components/MermaidDiagram.vue'
import PoolSimulator from './components/PoolSimulator.vue'
import SqlPlayground from './components/SqlPlayground.vue'
import StopAndThink from './components/StopAndThink.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('MermaidDiagram', MermaidDiagram)
    app.component('StopAndThink', StopAndThink)
    app.component('SqlPlayground', SqlPlayground)
    app.component('PoolSimulator', PoolSimulator)
    app.component('CacheSimulator', CacheSimulator)
  },
} satisfies Theme
