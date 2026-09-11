<template>
  <div class="config-view">
    <StaleBanner :is-stale="raw.isStale || merged.isStale" :last-good-update="raw.lastGoodUpdate || merged.lastGoodUpdate" />

    <div class="card toolbar-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <button class="btn primary" :disabled="refreshing" @click="refresh">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" :class="{ spinning: refreshing }">
              <polyline points="23 4 23 10 17 10"></polyline>
              <polyline points="1 20 1 14 7 14"></polyline>
              <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
            </svg>
            <span>{{ refreshing ? '刷新中…' : '拉取并重新合并' }}</span>
          </button>
          <span v-if="savedAt" class="muted update-time">最近生成：{{ formatTime(savedAt) }}</span>
        </div>

        <!-- 标签视图切换器 (移动端必显，桌面端也可以选择单栏/分栏) -->
        <div class="view-switch">
          <button class="switch-btn" :class="{ active: viewMode === 'split' }" @click="viewMode = 'split'">
            双栏对比
          </button>
          <button class="switch-btn" :class="{ active: viewMode === 'merged' }" @click="viewMode = 'merged'">
            合并后
          </button>
          <button class="switch-btn" :class="{ active: viewMode === 'raw' }" @click="viewMode = 'raw'">
            原始订阅
          </button>
        </div>
      </div>
    </div>

    <div class="panels" :class="`view-${viewMode}`">
      <!-- 原始配置面板 -->
      <div class="card panel" v-show="viewMode === 'split' || viewMode === 'raw'">
        <div class="panel-head">
          <div class="panel-title">
            <span class="panel-dot raw-dot"></span>
            <h3>原始订阅配置</h3>
          </div>
          <div class="panel-actions">
            <span class="badge-stat">{{ raw.lineCount }} 行 · {{ raw.ruleCount }} 规则</span>
            <button class="btn mini" @click="copyCode(raw.yaml, 'raw')">
              {{ copiedType === 'raw' ? '✓ 已复制' : '复制' }}
            </button>
            <button class="btn mini" @click="downloadYaml(raw.yaml, 'clash-raw.yaml')" title="下载原始 YAML">
              下载
            </button>
          </div>
        </div>
        <div class="code-container">
          <pre class="code mono">{{ raw.yaml || '— 无配置数据 —' }}</pre>
        </div>
      </div>

      <!-- 合并后配置面板 -->
      <div class="card panel" v-show="viewMode === 'split' || viewMode === 'merged'">
        <div class="panel-head">
          <div class="panel-title">
            <span class="panel-dot merged-dot"></span>
            <h3>最终合并配置</h3>
            <span class="active-tag">生效中</span>
          </div>
          <div class="panel-actions">
            <span class="badge-stat">{{ merged.lineCount }} 行 · {{ merged.ruleCount }} 规则</span>
            <button class="btn mini primary" @click="copyCode(merged.yaml, 'merged')">
              {{ copiedType === 'merged' ? '✓ 已复制' : '复制配置' }}
            </button>
            <button class="btn mini" @click="downloadYaml(merged.yaml, 'clash-merged.yaml')" title="下载合并后的 YAML">
              下载
            </button>
          </div>
        </div>
        <div class="code-container">
          <pre class="code mono">{{ merged.yaml || '— 无配置数据 —' }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import StaleBanner from '@/components/StaleBanner.vue'
import { configApi } from '@/api'
import type { ConfigDto } from '@/types'

const raw = ref<ConfigDto>({ yaml: '', lineCount: 0, ruleCount: 0, lastGoodUpdate: null, isStale: false })
const merged = ref<ConfigDto>({ yaml: '', lineCount: 0, ruleCount: 0, lastGoodUpdate: null, isStale: false })
const refreshing = ref(false)
const savedAt = ref<string | null>(null)
const viewMode = ref<'split' | 'merged' | 'raw'>('split')
const copiedType = ref<'raw' | 'merged' | null>(null)

async function load() {
  const [r, m] = await Promise.all([configApi.raw(), configApi.merged()])
  raw.value = r.data ?? raw.value
  merged.value = m.data ?? merged.value
  savedAt.value = new Date().toISOString()
}

async function refresh() {
  refreshing.value = true
  try {
    await configApi.refresh()
    await load()
  } finally {
    refreshing.value = false
  }
}

async function copyCode(text: string, type: 'raw' | 'merged') {
  if (!text) return
  await navigator.clipboard.writeText(text)
  copiedType.value = type
  setTimeout(() => {
    if (copiedType.value === type) copiedType.value = null
  }, 2000)
}

function downloadYaml(text: string, filename: string) {
  if (!text) return
  const blob = new Blob([text], { type: 'text/yaml;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

const formatTime = (iso: string) => new Date(iso).toLocaleString('zh-CN')

onMounted(() => {
  // Check if mobile on initial load
  if (window.innerWidth <= 860) {
    viewMode.value = 'merged'
  }
  load()
})
</script>

<style scoped>
.config-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.toolbar-card {
  padding: 12px 16px;
}
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}
.toolbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.spinning {
  animation: spin 1s linear infinite;
}
@keyframes spin {
  100% { transform: rotate(360deg); }
}

.view-switch {
  display: flex;
  background: var(--card-subtle);
  padding: 3px;
  border-radius: 8px;
  border: 1px solid var(--border);
  gap: 2px;
}
.switch-btn {
  background: none;
  border: none;
  padding: 5px 12px;
  border-radius: 6px;
  font-size: 13px;
  font-weight: 500;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.15s ease;
}
.switch-btn:hover {
  color: var(--text);
}
.switch-btn.active {
  background: var(--card);
  color: var(--brand);
  font-weight: 600;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
}

.panels {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
.panels.view-raw, .panels.view-merged {
  grid-template-columns: 1fr;
}

.panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
}

.panel-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.panel-title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.panel-title h3 {
  margin: 0;
  font-size: 15px;
  font-weight: 700;
}

.panel-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}
.raw-dot { background: var(--muted); }
.merged-dot { background: var(--brand); }

.active-tag {
  font-size: 11px;
  font-weight: 600;
  padding: 1px 6px;
  border-radius: 10px;
  background: rgba(47, 111, 237, 0.1);
  color: var(--brand);
}

.panel-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.badge-stat {
  font-size: 12px;
  color: var(--muted);
  font-family: "JetBrains Mono", monospace;
}

.btn.mini {
  padding: 3px 8px;
  font-size: 12px;
  min-height: 26px;
}

.code-container {
  border-radius: 8px;
  border: 1px solid var(--border);
  background: var(--bg);
  overflow: hidden;
}

.code {
  padding: 14px;
  max-height: 70vh;
  overflow: auto;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  margin: 0;
  color: var(--text);
  -webkit-overflow-scrolling: touch;
}
.mono {
  font-family: "JetBrains Mono", SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

@media (max-width: 860px) {
  .panels {
    grid-template-columns: 1fr;
  }
  .toolbar-left {
    width: 100%;
    justify-content: space-between;
  }
  .view-switch {
    width: 100%;
  }
  .view-switch .switch-btn {
    flex: 1;
    text-align: center;
  }
  .view-switch .switch-btn:first-child {
    display: none; /* Hide dual column on small screens */
  }
  .update-time {
    font-size: 12px;
  }
}
</style>