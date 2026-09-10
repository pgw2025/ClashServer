<template>
  <div class="dashboard">
    <StaleBanner :is-stale="dash.isStale" :last-good-update="dash.lastGoodUpdate" />

    <div class="cards">
      <div class="card stat">
        <div class="label">订阅地址</div>
        <div class="value mono">{{ dash.subUrl || '—' }}</div>
      </div>
      <div class="card stat">
        <div class="label">启用规则</div>
        <div class="value">{{ dash.enabledRuleCount }}</div>
      </div>
      <div class="card stat">
        <div class="label">节点数</div>
        <div class="value">{{ nodes.length }}</div>
      </div>
    </div>

    <div class="toolbar">
      <button class="btn primary" :disabled="testingAll" @click="runAllLatency">
        {{ testingAll ? '测速中…' : '全部测速' }}
      </button>
      <button class="btn" @click="toggleSort">{{ sortOrder === 'asc' ? '延时长→短' : '延时短→长' }}</button>
      <button class="btn" @click="refreshCache">立即刷新</button>
      <span class="muted" v-if="lastUpstreamResult">
        {{ lastUpstreamResult.ok ? '✅ 上游正常' : '❌ 上游不可达' }}
      </span>
    </div>

    <div class="node-grid">
      <div v-for="n in sortedNodes" :key="n.name" class="card node">
        <div class="node-head">
          <span class="node-name">{{ n.name }}</span>
          <span class="badge" :class="badgeClass(n.type)">{{ n.type }}</span>
        </div>
        <div class="muted">{{ n.server }}:{{ n.port }}</div>
        <div class="node-foot">
          <button class="btn" :disabled="testingMap[n.name]" @click="testLatency(n.name)">
            {{ testingMap[n.name] ? '…' : '测速' }}
          </button>
          <span v-if="n.latency != null" :class="latencyClass(n.latency)">{{ n.latency }} ms</span>
          <span v-else-if="n.error" class="danger-text">{{ n.error }}</span>
          <span v-else class="muted">--</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import StaleBanner from '@/components/StaleBanner.vue'
import { dashboardApi } from '@/api'
import type { DashboardDto, ProxyNode } from '@/types'

const dash = ref<DashboardDto>({ subUrl: '', enabledRuleCount: 0, nodeCount: 0, lastGoodUpdate: null, isStale: false })
const nodes = ref<ProxyNode[]>([])
const testingMap = ref<Record<string, boolean>>({})
const testingAll = ref(false)
const sortOrder = ref<'asc' | 'desc'>('asc')
const lastUpstreamResult = ref<{ ok: boolean } | null>(null)

async function load() {
  const [d, n] = await Promise.all([dashboardApi.get(), dashboardApi.nodes()])
  dash.value = d.data ?? dash.value
  nodes.value = n.data ?? []
}

async function testLatency(name: string) {
  testingMap.value[name] = true
  try {
    const res = await dashboardApi.latency(name)
    const node = nodes.value.find((x) => x.name === name)
    if (node) {
      node.latency = res.data?.latency ?? null
      node.error = res.data?.error ?? null
    }
    if (res.data?.ok === false && res.data.error) {
      // 保留错误信息
    }
  } finally {
    testingMap.value[name] = false
  }
}

// 全部测速：限制并发 4 路，避免瞬间大量 ICMP
async function runAllLatency() {
  testingAll.value = true
  const CONCURRENCY = 4
  const names = nodes.value.map((n) => n.name)
  let idx = 0
  async function worker() {
    while (idx < names.length) {
      const name = names[idx++]
      await testLatency(name)
    }
  }
  await Promise.all(Array.from({ length: Math.min(CONCURRENCY, names.length) }, () => worker()))
  testingAll.value = false
}

async function refreshCache() {
  try {
    await dashboardApi.refresh()
    await load()
  } catch { /* 后端返回 error */ }
}

async function testUpstream() {
  try {
    const res = await dashboardApi.health()
    lastUpstreamResult.value = { ok: res.data.ok }
  } catch {
    lastUpstreamResult.value = { ok: false }
  }
}

function toggleSort() {
  sortOrder.value = sortOrder.value === 'asc' ? 'desc' : 'asc'
}

const sortedNodes = computed(() => {
  const arr = [...nodes.value]
  const lat = (n: ProxyNode) => (n.latency == null ? Number.MAX_SAFE_INTEGER : n.latency)
  arr.sort((a, b) => (sortOrder.value === 'asc' ? lat(a) - lat(b) : lat(b) - lat(a)))
  return arr
})

const badgeClass = (t: string) => `badge-${t}`
const latencyClass = (v: number) => (v < 100 ? 'ok-text' : v < 300 ? 'brand' : v < 1000 ? 'warn' : 'danger-text')

onMounted(() => {
  load()
  testUpstream()
})
</script>

<style scoped>
.cards { display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 12px; margin-bottom: 16px; }
.stat .label { color: var(--muted); font-size: 12px; }
.stat .value { font-size: 18px; font-weight: 600; margin-top: 4px; }
.mono { font-family: monospace; font-size: 13px; word-break: break-all; }
.toolbar { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; }
.node-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
.node-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }
.node-name { font-weight: 600; }
.badge { font-size: 11px; padding: 2px 8px; border-radius: 10px; background: var(--bg); border: 1px solid var(--border); }
.node-foot { display: flex; justify-content: space-between; align-items: center; margin-top: 10px; }
.warn { color: #b8860b; }
@media (prefers-color-scheme: dark) { .warn { color: #d4a94e; } }
</style>