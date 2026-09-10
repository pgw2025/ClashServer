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
        <label>订阅 Token</label>
        <div class="token-row">
          <input v-model.trim="form.accessToken" type="password" />
          <button type="button" class="btn" @click="generateToken">生成 Token</button>
        </div>
        <p class="hint">仅用于客户端订阅鉴权（/sub 端点），管理端登录请使用下方用户名密码。</p>
      </div>

      <div class="field">
        <label>管理员用户名</label>
        <input v-model.trim="form.username" autocomplete="username" placeholder="登录用户名" />
        <p class="hint">管理端登录用户名；修改后所有已登录会话将失效，需重新登录。</p>
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

  <div class="card" v-if="loaded">
    <h2>修改密码</h2>
    <form @submit.prevent="changePassword">
      <div class="field">
        <label>当前密码</label>
        <input v-model="pwd.currentPassword" type="password" autocomplete="current-password" required />
      </div>
      <div class="field">
        <label>新密码（8-64 位）</label>
        <input v-model="pwd.newPassword" type="password" autocomplete="new-password" minlength="8" maxlength="64" required />
      </div>
      <div class="field">
        <label>确认新密码</label>
        <input v-model="pwd.confirmPassword" type="password" autocomplete="new-password" required />
      </div>
      <p v-if="pwdError" class="danger-text">{{ pwdError }}</p>
      <div class="actions">
        <button type="submit" class="btn primary" :disabled="pwdSaving">{{ pwdSaving ? '修改中…' : '修改密码' }}</button>
        <span v-if="pwdOk" class="muted">修改成功，其他登录会话已下线</span>
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
  username: '',
  cacheMinutes: 15,
  adminFetchTimeoutSeconds: 5,
  publicSubFetchTimeoutSeconds: 10,
  insertRulesBefore: true,
  replaceMode: false,
  autoGroupNodes: true
})

const pwd = reactive({ currentPassword: '', newPassword: '', confirmPassword: '' })
const pwdSaving = ref(false)
const pwdError = ref('')
const pwdOk = ref(false)

async function reload() {
  loaded.value = false
  saveError.value = ''
  const res = await settingsApi.get()
  const d = res.data
  if (d) {
    form.upstreamUrl = d.upstreamUrl ?? ''
    form.accessToken = d.accessToken ?? ''
    form.username = d.username ?? ''
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
    username: form.username || null,
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

async function changePassword() {
  pwdError.value = ''
  pwdOk.value = false
  if (pwd.newPassword !== pwd.confirmPassword) {
    pwdError.value = '两次输入的新密码不一致'
    return
  }
  if (pwd.newPassword.length < 8) {
    pwdError.value = '新密码至少 8 位'
    return
  }
  pwdSaving.value = true
  try {
    const res = await settingsApi.changePassword(pwd.currentPassword, pwd.newPassword)
    if (!res.ok) { pwdError.value = res.error ?? '修改失败' }
    else {
      pwdOk.value = true
      pwd.currentPassword = ''
      pwd.newPassword = ''
      pwd.confirmPassword = ''
    }
  } catch (err) {
    pwdError.value = (err as any)?.response?.data?.error ?? '修改失败'
  } finally {
    pwdSaving.value = false
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