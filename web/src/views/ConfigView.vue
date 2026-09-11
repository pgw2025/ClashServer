<template>
  <div>
    <StaleBanner :is-stale="raw.isStale || merged.isStale" :last-good-update="raw.lastGoodUpdate || merged.lastGoodUpdate" />

    <div class="toolbar">
      <button class="btn" :disabled="refreshing" @click="refresh">{{ refreshing ? '刷新中…' : '手动刷新' }}</button>
      <span v-if="savedAt" class="muted update-time">最近加载：{{ formatTime(savedAt) }}</span>

      <!-- 移动端标签切换 -->
      <div class="mobile-tabs">
        <button class="tab-btn" :class="{ active: activeTab === 'raw' }" @click="activeTab = 'raw'">原始配置</button>
        <button class="tab-btn" :class="{ active: activeTab === 'merged' }" @click="activeTab = 'merged'">合并后配置</button>
      </div>
    </div>

    <div class="panels">
      <div class="card panel" :class="{ 'mobile-hidden': activeTab !== 'raw' }">
        <div class="panel-head">
          <h3>原始配置</h3>
          <div class="panel-actions">
            <span class="muted">{{ raw.lineCount }} 行 · {{ raw.ruleCount }} 规则</span>
            <button class="btn mini" @click="copyCode(raw.yaml)">复制</button>
          </div>
        </div>
        <pre class="code mono">{{ raw.yaml || '—' }}</pre>
      </div>
      <div class="card panel" :class="{ 'mobile-hidden': activeTab !== 'merged' }">
        <div class="panel-head">
          <h3>合并后配置</h3>
          <div class="panel-actions">
            <span class="muted">{{ merged.lineCount }} 行 · {{ merged.ruleCount }} 规则</span>
            <button class="btn mini" @click="copyCode(merged.yaml)">复制</button>
          </div>
        </div>
        <pre class="code mono">{{ merged.yaml || '—' }}</pre>
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
const activeTab = ref<'raw' | 'merged'>('merged')

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

async function copyCode(text: string) {
  if (!text) return
  await navigator.clipboard.writeText(text)
  alert('已复制配置内容到剪贴板')
}

const formatTime = (iso: string) => new Date(iso).toLocaleString('zh-CN')

onMounted(load)
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; align-items: center; margin-bottom: 12px; flex-wrap: wrap; }
.panels { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.panel-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
.panel-actions { display: flex; align-items: center; gap: 8px; }
.panel-head h3 { margin: 0; font-size: 16px; }
.mobile-tabs { display: none; gap: 4px; margin-left: auto; }
.tab-btn {
  background: var(--bg);
  border: 1px solid var(--border);
  padding: 4px 12px;
  border-radius: 6px;
  font-size: 13px;
  cursor: pointer;
  color: var(--muted);
}
.tab-btn.active {
  background: var(--brand);
  color: #fff;
  border-color: var(--brand);
  font-weight: 500;
}
.btn.mini { padding: 2px 8px; font-size: 12px; }

.code {
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px;
  max-height: 70vh;
  overflow: auto;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-all;
  margin: 0;
  -webkit-overflow-scrolling: touch;
}
.mono { font-family: monospace; }

@media (max-width: 900px) {
  .panels { grid-template-columns: 1fr; }
  .mobile-tabs { display: flex; width: 100%; margin-top: 6px; }
  .mobile-tabs .tab-btn { flex: 1; text-align: center; }
  .mobile-hidden { display: none !important; }
  .update-time { font-size: 12px; }
}
</style>