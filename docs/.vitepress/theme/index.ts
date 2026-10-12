import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import CacheSimulator from './components/CacheSimulator.vue'
import HashRingSimulator from './components/HashRingSimulator.vue'
import MermaidDiagram from './components/MermaidDiagram.vue'
import PoolSimulator from './components/PoolSimulator.vue'
import ReplicaSimulator from './components/ReplicaSimulator.vue'
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
    app.component('ReplicaSimulator', ReplicaSimulator)
    app.component('HashRingSimulator', HashRingSimulator)
  },
} satisfies Theme
