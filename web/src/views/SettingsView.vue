<template>
  <div class="card">
    <h2>设置</h2>

    <div v-if="!loaded" class="muted">加载中…</div>

    <form v-else @submit.prevent="save">
      <div class="field">
        <label>上游订阅 URL</label>
        <input v-model.trim="form.upstreamUrl" placeholder="https://…" />
        <p class="hint">代理订阅地址，后台定时抓取并合并自定义规则。</p>
      </div>

      <div class="field">
        <label>访问 Token</label>
        <div class="token-row">
          <input v-model.trim="form.accessToken" type="password" />
          <button type="button" class="btn" @click="generateToken">生成 Token</button>
        </div>
        <p class="hint">用于管理端登录与订阅鉴权（如配置，则订阅需携带 token）。生成后需点「保存」才会生效。</p>
      </div>

      <div class="grid">
        <div class="field">
          <label>缓存时长（分钟）</label>
          <input v-model.number="form.cacheMinutes" type="number" min="1" max="1440" />
        </div>
        <div class="field">
          <label>管理页面抓取超时（秒）</label>
          <input v-model.number="form.adminFetchTimeoutSeconds" type="number" min="1" max="60" />
        </div>
        <div class="field">
          <label>订阅端点抓取超时（秒）</label>
          <input v-model.number="form.publicSubFetchTimeoutSeconds" type="number" min="1" max="60" />
        </div>
      </div>

      <div class="field">
        <label class="check"><input type="checkbox" v-model="form.insertRulesBefore" /> 自定义规则插入到订阅之前</label>
      </div>
      <div class="field">
        <label class="check"><input type="checkbox" v-model="form.replaceMode" /> 完全替换模式（忽略上游规则）</label>
      </div>
      <div class="field">
        <label class="check"><input type="checkbox" v-model="form.autoGroupNodes" /> 自动分组节点</label>
      </div>

      <p v-if="saveError" class="danger-text">{{ saveError }}</p>

      <div class="actions">
        <button type="submit" class="btn primary" :disabled="saving">{{ saving ? '保存中…' : '保存' }}</button>
        <button type="button" class="btn" @click="reload">重置</button>
        <span v-if="savedAt" class="muted">最近保存：{{ formatTime(savedAt) }}</span>
      </div>
    </form>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { settingsApi } from '@/api'
import type { SettingsDto } from '@/types'

const loaded = ref(false)
const saving = ref(false)
const savedAt = ref<string | null>(null)
const saveError = ref('')

const form = reactive({
  upstreamUrl: '',
  accessToken: '',
  cacheMinutes: 15,
  adminFetchTimeoutSeconds: 5,
  publicSubFetchTimeoutSeconds: 10,
  insertRulesBefore: true,
  replaceMode: false,
  autoGroupNodes: true
})

async function reload() {
  loaded.value = false
  saveError.value = ''
  const res = await settingsApi.get()
  const d = res.data
  if (d) {
    form.upstreamUrl = d.upstreamUrl ?? ''
    form.accessToken = d.accessToken ?? ''
    form.cacheMinutes = d.cacheMinutes
    form.adminFetchTimeoutSeconds = d.adminFetchTimeoutSeconds
    form.publicSubFetchTimeoutSeconds = d.publicSubFetchTimeoutSeconds
    form.insertRulesBefore = d.insertRulesBefore
    form.replaceMode = d.replaceMode
    form.autoGroupNodes = d.autoGroupNodes
    savedAt.value = d.updatedAt
  }
  loaded.value = true
}

async function save() {
  saveError.value = ''
  const dto = {
    upstreamUrl: form.upstreamUrl || null,
    accessToken: form.accessToken || null,
    cacheMinutes: form.cacheMinutes,
    adminFetchTimeoutSeconds: form.adminFetchTimeoutSeconds,
    publicSubFetchTimeoutSeconds: form.publicSubFetchTimeoutSeconds,
    insertRulesBefore: form.insertRulesBefore,
    replaceMode: form.replaceMode,
    autoGroupNodes: form.autoGroupNodes
  }
  saving.value = true
  try {
    const res = await settingsApi.save(dto)
    if (!res.ok) { saveError.value = res.error ?? '保存失败' }
    else { savedAt.value = res.data?.updatedAt ?? null; await reload() }
  } catch (err) {
    saveError.value = (err as any)?.response?.data?.error ?? '保存失败'
  } finally {
    saving.value = false
  }
}

async function generateToken() {
  try {
    const res = await settingsApi.generateToken()
    if (res.data?.token) form.accessToken = res.data.token
  } catch (err) {
    saveError.value = (err as any)?.response?.data?.error ?? '生成 Token 失败'
  }
}

const formatTime = (iso: string) => (iso ? new Date(iso).toLocaleString('zh-CN') : '')

onMounted(reload)
</script>

<style scoped>
.field { margin-bottom: 16px; }
.field label { display: block; font-weight: 600; margin-bottom: 4px; }
.field input[type='text'], .field input[type='password'], .field input[type='number'] { width: 100%; }
.token-row { display: flex; gap: 8px; }
.token-row input { flex: 1; }
.grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
@media (max-width: 700px) { .grid { grid-template-columns: 1fr; } }
.check { display: flex; align-items: center; gap: 8px; font-weight: 400 !important; }
.hint { color: var(--muted); font-size: 12px; margin-top: 4px; }
.actions { display: flex; gap: 8px; align-items: center; }
</style>