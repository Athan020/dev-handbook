import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import BacklogSimulator from './components/BacklogSimulator.vue'
import CacheSimulator from './components/CacheSimulator.vue'
import DeliverySimulator from './components/DeliverySimulator.vue'
import HashRingSimulator from './components/HashRingSimulator.vue'
import InterleavingSimulator from './components/InterleavingSimulator.vue'
import MermaidDiagram from './components/MermaidDiagram.vue'
import OutboxSimulator from './components/OutboxSimulator.vue'
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
    app.component('DeliverySimulator', DeliverySimulator)
    app.component('OutboxSimulator', OutboxSimulator)
    app.component('BacklogSimulator', BacklogSimulator)
    app.component('InterleavingSimulator', InterleavingSimulator)
  },
} satisfies Theme
