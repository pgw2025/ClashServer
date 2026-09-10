<template>
  <div>
    <StaleBanner :is-stale="raw.isStale || merged.isStale" :last-good-update="raw.lastGoodUpdate || merged.lastGoodUpdate" />

    <div class="toolbar">
      <button class="btn" :disabled="refreshing" @click="refresh">{{ refreshing ? '刷新中…' : '手动刷新' }}</button>
      <span v-if="savedAt" class="muted">最近加载：{{ formatTime(savedAt) }}</span>
    </div>

    <div class="panels">
      <div class="card panel">
        <div class="panel-head">
          <h3>原始配置</h3>
          <span class="muted">{{ raw.lineCount }} 行 · {{ raw.ruleCount }} 规则</span>
        </div>
        <pre class="code mono">{{ raw.yaml || '—' }}</pre>
      </div>
      <div class="card panel">
        <div class="panel-head">
          <h3>合并后配置</h3>
          <span class="muted">{{ merged.lineCount }} 行 · {{ merged.ruleCount }} 规则</span>
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

const formatTime = (iso: string) => new Date(iso).toLocaleString('zh-CN')

onMounted(load)
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; align-items: center; margin-bottom: 12px; }
.panels { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
@media (max-width: 900px) { .panels { grid-template-columns: 1fr; } }
.panel-head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 8px; }
.panel-head h3 { margin: 0; }
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
}
.mono { font-family: monospace; }
</style>