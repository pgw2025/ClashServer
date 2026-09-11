<template>
  <div class="dashboard">
    <StaleBanner :is-stale="dash.isStale" :last-good-update="dash.lastGoodUpdate" />

    <!-- 顶部概览资产与指标区 -->
    <div class="hero-grid">
      <!-- 专属订阅凭据卡片 -->
      <div class="card sub-card">
        <div class="sub-header">
          <div class="sub-title-group">
            <div class="sub-icon">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"></path>
                <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"></path>
              </svg>
            </div>
            <div>
              <div class="sub-title">专属 Clash 订阅链接</div>
              <div class="sub-desc">已自动合并自定义规则，可直接导入客户端</div>
            </div>
          </div>
          <span class="secure-tag">安全加密</span>
        </div>

        <div class="sub-input-box">
          <span class="sub-url mono" :title="dash.subUrl">{{ dash.subUrl || '正在生成订阅链接…' }}</span>
          <div class="sub-btns">
            <button class="btn mini primary" :disabled="!dash.subUrl" @click="copySub">
              <svg v-if="!copied" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect width="14" height="14" x="8" y="8" rx="2" ry="2"></rect>
                <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"></path>
              </svg>
              <span>{{ copied ? '已复制 ✓' : '复制链接' }}</span>
            </button>
            <a v-if="clashSchemeUrl" :href="clashSchemeUrl" class="btn mini clash-btn" title="唤起本地 Clash 客户端一键导入">
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
              <span>导入 Clash</span>
            </a>
          </div>
        </div>
      </div>

      <!-- 右侧两列指标 -->
      <div class="metric-col">
        <div class="card metric-card">
          <div class="metric-top">
            <span class="metric-label">生效规则</span>
            <span class="metric-badge green">ACTIVE</span>
          </div>
          <div class="metric-value-row">
            <span class="metric-num">{{ dash.enabledRuleCount }}</span>
            <span class="metric-unit">条自定义规则</span>
          </div>
        </div>

        <div class="card metric-card">
          <div class="metric-top">
            <span class="metric-label">节点池健康度</span>
            <div class="health-status">
              <span class="pulse-dot" :class="lastUpstreamResult?.ok ? 'online' : 'offline'"></span>
              <span class="health-text">{{ lastUpstreamResult?.ok ? '上游正常' : '不可达' }}</span>
            </div>
          </div>
          <div class="metric-value-row">
            <span class="metric-num">{{ nodes.length }}</span>
            <span class="metric-unit">个代理节点</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 操作工具条与协议筛选 -->
    <div class="toolbar-section">
      <div class="main-actions">
        <button class="btn primary" :disabled="testingAll" @click="runAllLatency">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
            <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
          </svg>
          <span>{{ testingAll ? '测速进行中…' : '全部测速' }}</span>
        </button>
        <button class="btn" @click="toggleSort">
          <span>排序: {{ sortOrder === 'asc' ? '延时由低到高 ↑' : '延时由高到低 ↓' }}</span>
        </button>
        <button class="btn" @click="refreshCache" title="强制从上游订阅拉取最新节点并清空本地缓存">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"></path>
          </svg>
          <span>刷新缓存</span>
        </button>
      </div>

      <!-- 节点协议快速筛选 -->
      <div class="type-filter" v-if="availableTypes.length > 1">
        <button 
          class="filter-pill" 
          :class="{ active: selectedType === 'ALL' }" 
          @click="selectedType = 'ALL'">
          全部 ({{ nodes.length }})
        </button>
        <button 
          v-for="t in availableTypes" 
          :key="t" 
          class="filter-pill" 
          :class="{ active: selectedType === t }" 
          @click="selectedType = t">
          {{ t.toUpperCase() }}
        </button>
      </div>
    </div>

    <!-- 节点网格卡片 -->
    <div class="node-grid">
      <div v-for="n in filteredAndSortedNodes" :key="n.name" class="card node-card">
        <div class="node-head">
          <div class="node-name-wrap" :title="n.name">
            <span class="node-name">{{ n.name }}</span>
          </div>
          <span class="proto-tag" :class="protoClass(n.type)">{{ n.type }}</span>
        </div>

        <div class="node-server-row mono">
          <span class="server-host">{{ n.server }}</span>
          <span class="server-port">:{{ n.port }}</span>
        </div>

        <div class="node-foot">
          <button class="btn mini ping-btn" :disabled="testingMap[n.name]" @click="testLatency(n.name)">
            <span>{{ testingMap[n.name] ? '测速中' : '测速' }}</span>
          </button>
          
          <div class="latency-result">
            <span v-if="n.latency != null" class="latency-badge" :class="latencyBadgeClass(n.latency)">
              {{ n.latency }} ms
            </span>
            <span v-else-if="n.error" class="latency-error" :title="n.error">不可达</span>
            <span v-else class="latency-empty">未测速</span>
          </div>
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
const copied = ref(false)
const selectedType = ref('ALL')

const clashSchemeUrl = computed(() => {
  if (!dash.value.subUrl) return ''
  return `clash://install-config?url=${encodeURIComponent(dash.value.subUrl)}&name=ClashServer`
})

const availableTypes = computed(() => {
  const set = new Set<string>()
  for (const n of nodes.value) {
    if (n.type) set.add(n.type.toLowerCase())
  }
  return Array.from(set)
})

async function copySub() {
  if (!dash.value.subUrl) return
  try {
    await navigator.clipboard.writeText(dash.value.subUrl)
    copied.value = true
    setTimeout(() => { copied.value = false }, 2000)
  } catch {
    const ta = document.createElement('textarea')
    ta.value = dash.value.subUrl
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    copied.value = true
    setTimeout(() => { copied.value = false }, 2000)
  }
}

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
  } finally {
    testingMap.value[name] = false
  }
}

// 全部测速：限制并发 4 路
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
  } catch { /* ignore */ }
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

const filteredAndSortedNodes = computed(() => {
  let list = nodes.value
  if (selectedType.value !== 'ALL') {
    list = list.filter((n) => n.type.toLowerCase() === selectedType.value.toLowerCase())
  }
  const arr = [...list]
  const lat = (n: ProxyNode) => (n.latency == null ? Number.MAX_SAFE_INTEGER : n.latency)
  arr.sort((a, b) => (sortOrder.value === 'asc' ? lat(a) - lat(b) : lat(b) - lat(a)))
  return arr
})

const protoClass = (t: string) => {
  const low = (t || '').toLowerCase()
  if (low.includes('ss')) return 'proto-ss'
  if (low.includes('vmess')) return 'proto-vmess'
  if (low.includes('trojan')) return 'proto-trojan'
  if (low.includes('vless')) return 'proto-vless'
  return 'proto-other'
}

const latencyBadgeClass = (v: number) => {
  if (v < 120) return 'lat-fast'
  if (v < 280) return 'lat-med'
  if (v < 800) return 'lat-slow'
  return 'lat-timeout'
}

onMounted(() => {
  load()
  testUpstream()
})
</script>

<style scoped>
.dashboard { display: flex; flex-direction: column; gap: 20px; }

/* 英雄概览区 */
.hero-grid {
  display: grid;
  grid-template-columns: 1.6fr 1fr;
  gap: 16px;
}

/* 订阅卡片 */
.sub-card {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 16px;
  background: linear-gradient(135deg, var(--card) 0%, var(--card-subtle) 100%);
  border: 1px solid var(--border);
  position: relative;
  overflow: hidden;
}
.sub-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}
.sub-title-group {
  display: flex;
  align-items: center;
  gap: 12px;
}
.sub-icon {
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: var(--brand-subtle);
  color: var(--brand);
  display: flex;
  align-items: center;
  justify-content: center;
}
.sub-title {
  font-weight: 700;
  font-size: 15px;
  color: var(--text);
}
.sub-desc {
  font-size: 12.5px;
  color: var(--muted);
  margin-top: 2px;
}
.secure-tag {
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  background: var(--ok-subtle);
  color: var(--ok);
  border-radius: 6px;
}

.sub-input-box {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  background: var(--card);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 6px 10px;
}
.sub-url {
  font-size: 13px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}
.sub-btns {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
}
.clash-btn {
  background: var(--card-subtle);
}

/* 指标列 */
.metric-col {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
.metric-card {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 16px 18px;
}
.metric-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.metric-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--muted);
}
.metric-badge.green {
  font-size: 10.5px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--ok-subtle);
  color: var(--ok);
  letter-spacing: 0.04em;
}
.health-status {
  display: flex;
  align-items: center;
  gap: 6px;
}
.health-text {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
}
.metric-value-row {
  margin-top: 14px;
  display: flex;
  align-items: baseline;
  gap: 6px;
}
.metric-num {
  font-size: 28px;
  font-weight: 700;
  color: var(--text);
  line-height: 1;
}
.metric-unit {
  font-size: 12px;
  color: var(--muted);
}

/* 操作工具栏与分类筛选 */
.toolbar-section {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.main-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.type-filter {
  display: flex;
  align-items: center;
  gap: 4px;
  background: var(--card);
  padding: 3px;
  border-radius: 8px;
  border: 1px solid var(--border);
}
.filter-pill {
  background: none;
  border: none;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
  color: var(--muted);
  cursor: pointer;
  transition: all 0.15s ease;
}
.filter-pill.active {
  background: var(--brand);
  color: #ffffff;
}

/* 节点网格 */
.node-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 14px;
}
.node-card {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 14px 16px;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}
.node-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-md);
  border-color: var(--border-hover);
}
.node-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}
.node-name-wrap {
  flex: 1;
  overflow: hidden;
}
.node-name {
  font-weight: 600;
  font-size: 14px;
  color: var(--text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: block;
}
.proto-tag {
  font-size: 10.5px;
  font-weight: 700;
  padding: 2px 7px;
  border-radius: 4px;
  text-transform: uppercase;
  flex-shrink: 0;
}
.proto-ss { background: #eff6ff; color: #2563eb; border: 1px solid #bfdbfe; }
.proto-vmess { background: #fdf2f8; color: #db2777; border: 1px solid #fbcfe8; }
.proto-trojan { background: #f5f3ff; color: #7c3aed; border: 1px solid #ddd6fe; }
.proto-vless { background: #ecfdf5; color: #059669; border: 1px solid #a7f3d0; }
.proto-other { background: var(--card-subtle); color: var(--muted); border: 1px solid var(--border); }

.node-server-row {
  margin-top: 6px;
  font-size: 12px;
  color: var(--muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.server-port {
  opacity: 0.7;
}

.node-foot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 14px;
  padding-top: 10px;
  border-top: 1px solid var(--border);
}
.ping-btn {
  font-size: 11.5px;
  padding: 3px 8px;
}
.latency-badge {
  font-size: 12px;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 6px;
}
.lat-fast { background: var(--ok-subtle); color: var(--ok); }
.lat-med { background: var(--brand-subtle); color: var(--brand); }
.lat-slow { background: var(--warning-subtle); color: var(--warning); }
.lat-timeout { background: var(--danger-subtle); color: var(--danger); }
.latency-error { font-size: 11.5px; color: var(--danger); font-weight: 500; }
.latency-empty { font-size: 11.5px; color: var(--muted); }

.mono { font-family: "JetBrains Mono", SFMono-Regular, Menlo, Monaco, Consolas, monospace; }

@media (max-width: 900px) {
  .hero-grid { grid-template-columns: 1fr; }
  .metric-col { grid-template-columns: 1fr 1fr; }
}
@media (max-width: 640px) {
  .metric-col { grid-template-columns: 1fr; }
  .sub-input-box { flex-direction: column; align-items: stretch; }
  .sub-btns { justify-content: flex-end; }
  .toolbar-section { flex-direction: column; align-items: stretch; }
  .main-actions { width: 100%; }
  .main-actions .btn { flex: 1 1 calc(50% - 6px); }
  .type-filter { width: 100%; overflow-x: auto; }
}

@media (prefers-color-scheme: dark) {
  .proto-ss { background: #1e3a8a; color: #93c5fd; border-color: #1d4ed8; }
  .proto-vmess { background: #831843; color: #f472b6; border-color: #9d174d; }
  .proto-trojan { background: #2e1065; color: #c4b5fd; border-color: #5b21b6; }
  .proto-vless { background: #064e3b; color: #6ee7b7; border-color: #047857; }
}
</style>