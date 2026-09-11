<template>
  <div class="settings-view">
    <div v-if="!loaded" class="card loading-card">
      <div class="spinner"></div>
      <p class="muted">正在加载配置参数…</p>
    </div>

    <template v-else>
      <!-- 上游与订阅参数 -->
      <div class="card setting-section">
        <div class="section-title">
          <div class="title-icon">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"></path>
              <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"></path>
            </svg>
          </div>
          <div>
            <h2>订阅与核心参数</h2>
            <p class="muted subtitle">配置上游服务商订阅链接及对外分发凭证。</p>
          </div>
        </div>

        <form @submit.prevent="save" class="setting-form">
          <div class="field">
            <label>上游订阅 URL</label>
            <input v-model.trim="form.upstreamUrl" placeholder="https://provider.example.com/api/v1/client/subscribe?token=..." />
            <p class="hint">服务商提供的原始 Clash 订阅地址，服务端后台自动定时同步。</p>
          </div>

          <div class="field">
            <label>客户端订阅 Token</label>
            <div class="token-row">
              <input
                v-model.trim="form.accessToken"
                :type="showToken ? 'text' : 'password'"
                class="mono"
                placeholder="留空则订阅不设访问令牌"
              />
              <button type="button" class="btn" @click="showToken = !showToken">
                {{ showToken ? '隐藏' : '显示' }}
              </button>
              <button type="button" class="btn" @click="copyToken">
                {{ tokenCopied ? '✓ 已复制' : '复制' }}
              </button>
              <button type="button" class="btn primary" @click="generateToken">生成新 Token</button>
            </div>
            <p class="hint">用于客户端拉取合并订阅（/sub?token=...）时的身份鉴权。</p>
          </div>

          <div class="field">
            <label>管理员登录用户名</label>
            <input v-model.trim="form.username" autocomplete="username" placeholder="默认 admin" />
            <p class="hint">控制台登录用户名；更新保存后需使用新用户名重新登录。</p>
          </div>

          <div class="divider"></div>

          <div class="section-subtitle">抓取与合并行为</div>

          <div class="grid">
            <div class="field">
              <label>缓存时长 (分钟)</label>
              <input v-model.number="form.cacheMinutes" type="number" inputmode="numeric" min="1" max="1440" />
              <p class="hint">命中缓存期间无需重复向上游请求。</p>
            </div>
            <div class="field">
              <label>后台同步超时 (秒)</label>
              <input v-model.number="form.adminFetchTimeoutSeconds" type="number" inputmode="numeric" min="1" max="60" />
              <p class="hint">定时刷新上游的超时时间。</p>
            </div>
            <div class="field">
              <label>客户端拉取超时 (秒)</label>
              <input v-model.number="form.publicSubFetchTimeoutSeconds" type="number" inputmode="numeric" min="1" max="60" />
              <p class="hint">客户端访问时如遇缓存过期拉取的超时。</p>
            </div>
          </div>

          <div class="switch-list">
            <div class="switch-item">
              <div class="switch-info">
                <span class="switch-title">自定义规则置前</span>
                <span class="switch-desc">将自定义路由规则插入到上游规则最前面，享受更高匹配优先级。</span>
              </div>
              <label class="switch-toggle">
                <input type="checkbox" v-model="form.insertRulesBefore" />
                <span class="switch-slider"></span>
              </label>
            </div>

            <div class="switch-item">
              <div class="switch-info">
                <span class="switch-title">完全替换模式</span>
                <span class="switch-desc">完全忽略上游订阅中的所有规则，仅保留自建自定义规则。</span>
              </div>
              <label class="switch-toggle">
                <input type="checkbox" v-model="form.replaceMode" />
                <span class="switch-slider"></span>
              </label>
            </div>

            <div class="switch-item">
              <div class="switch-info">
                <span class="switch-title">自动分组节点</span>
                <span class="switch-desc">自动将节点按地区国家和特征组织策略组，优化节点选择体验。</span>
              </div>
              <label class="switch-toggle">
                <input type="checkbox" v-model="form.autoGroupNodes" />
                <span class="switch-slider"></span>
              </label>
            </div>
          </div>

          <div v-if="saveError" class="feedback-banner error-banner">⚠️ {{ saveError }}</div>
          <div v-if="saveSuccessMsg" class="feedback-banner success-banner">✓ {{ saveSuccessMsg }}</div>

          <div class="actions">
            <button type="submit" class="btn primary save-btn" :disabled="saving">
              {{ saving ? '正在保存…' : '保存设置' }}
            </button>
            <button type="button" class="btn" @click="reload">重置</button>
            <span v-if="savedAt" class="muted update-time">最近保存于：{{ formatTime(savedAt) }}</span>
          </div>
        </form>
      </div>

      <!-- 密码修改 -->
      <div class="card setting-section">
        <div class="section-title">
          <div class="title-icon">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
              <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
            </svg>
          </div>
          <div>
            <h2>修改管理密码</h2>
            <p class="muted subtitle">保障控制台安全，修改密码后将自动使其他登录会话失效。</p>
          </div>
        </div>

        <form @submit.prevent="changePassword" class="setting-form pwd-form">
          <div class="field">
            <label>当前旧密码</label>
            <input v-model="pwd.currentPassword" type="password" autocomplete="current-password" required placeholder="输入当前密码" />
          </div>
          <div class="grid pwd-grid">
            <div class="field">
              <label>新密码（8-64 位）</label>
              <input v-model="pwd.newPassword" type="password" autocomplete="new-password" minlength="8" maxlength="64" required placeholder="至少 8 位" />
            </div>
            <div class="field">
              <label>确认新密码</label>
              <input v-model="pwd.confirmPassword" type="password" autocomplete="new-password" required placeholder="再次输入新密码" />
            </div>
          </div>

          <div v-if="pwdError" class="feedback-banner error-banner">⚠️ {{ pwdError }}</div>
          <div v-if="pwdOk" class="feedback-banner success-banner">✓ 密码修改成功！已注销其他设备会话。</div>

          <div class="actions">
            <button type="submit" class="btn primary" :disabled="pwdSaving">
              {{ pwdSaving ? '修改中…' : '确认修改密码' }}
            </button>
          </div>
        </form>
      </div>

      <!-- 数据备份与还原 -->
      <div class="card setting-section">
        <div class="section-title">
          <div class="title-icon">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
              <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
              <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
            </svg>
          </div>
          <div>
            <h2>数据备份与还原</h2>
            <p class="muted subtitle">将全部配置与自定义规则打包为 zip 导出，或从 zip 完整还原（还原前自动备份当前数据）。</p>
          </div>
        </div>

        <div class="field">
          <button type="button" class="btn primary" :disabled="exporting" @click="doExport">
            {{ exporting ? '正在导出…' : '导出全部数据 (zip)' }}
          </button>
          <p class="hint">导出文件包含全部配置（含订阅 Token、上游链接）；属敏感数据，请妥善保管，切勿泄露给他人。</p>
        </div>

        <div class="divider"></div>

        <div class="field">
          <label>从备份还原</label>
          <input ref="fileInput" type="file" accept=".zip" :disabled="backupBusy" @change="pickFile" />
          <p class="hint">选择此前导出的 zip 备份文件进行还原（需两步确认）。</p>
        </div>

        <div v-if="backupPreview" class="restore-panel">
          <p class="restore-title">将还原以下内容：</p>
          <ul class="restore-list">
            <li>配置参数{{ backupPreview.containsSettings ? '' : '（备份中不含配置）' }}</li>
            <li>自定义规则 {{ backupPreview.ruleCount }} 条</li>
          </ul>
          <p class="hint">导出时间：{{ formatTime(backupPreview.exportedAt) }} · 备份格式 v{{ backupPreview.formatVersion }}</p>
          <p class="restore-warn">此操作将覆盖当前配置与规则；还原前会自动备份当前数据到服务端，可回滚。</p>
          <div class="actions">
            <button type="button" class="btn primary" :disabled="backupBusy" @click="confirmImport">
              {{ backupBusy ? '还原中…' : '确认还原' }}
            </button>
            <button type="button" class="btn" :disabled="backupBusy" @click="resetBackup">取消</button>
          </div>
        </div>

        <div v-if="backupResult" class="feedback-banner success-banner backup-result">
          ✓ 还原成功：{{ backupResult.ruleCount }} 条自定义规则
          <div class="hint">
            已还原条目：{{ backupResult.restoredEntries.join('、') }}；还原前数据已备份至 {{ backupResult.backupDir }}
          </div>
          <div v-if="backupResult.credentialsChanged" class="hint restore-warn">
            ⚠️ 管理凭据已随备份变更，本次会话可能失效，请重新登录。
          </div>
        </div>

        <div v-if="backupError" class="feedback-banner error-banner">⚠️ {{ backupError }}</div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { settingsApi, backupApi } from '@/api'
import type { SettingsDto, BackupPreview, BackupImportResult } from '@/types'

const loaded = ref(false)
const saving = ref(false)
const savedAt = ref<string | null>(null)
const saveError = ref('')
const saveSuccessMsg = ref('')
const showToken = ref(false)
const tokenCopied = ref(false)

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
  saveSuccessMsg.value = ''
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

async function copyToken() {
  if (!form.accessToken) return
  await navigator.clipboard.writeText(form.accessToken)
  tokenCopied.value = true
  setTimeout(() => { tokenCopied.value = false }, 2000)
}

async function save() {
  saveError.value = ''
  saveSuccessMsg.value = ''
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
    if (!res.ok) {
      saveError.value = res.error ?? '保存失败'
    } else {
      savedAt.value = res.data?.updatedAt ?? null
      saveSuccessMsg.value = '设置已成功保存并立即生效'
      setTimeout(() => { saveSuccessMsg.value = '' }, 4000)
    }
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
    if (!res.ok) {
      pwdError.value = res.error ?? '修改失败'
    } else {
      pwdOk.value = true
      pwd.currentPassword = ''
      pwd.newPassword = ''
      pwd.confirmPassword = ''
      setTimeout(() => { pwdOk.value = false }, 5000)
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
    if (res.data?.token) {
      form.accessToken = res.data.token
      showToken.value = true
    }
  } catch (err) {
    saveError.value = (err as any)?.response?.data?.error ?? '生成 Token 失败'
  }
}

const formatTime = (iso: string) => (iso ? new Date(iso).toLocaleString('zh-CN') : '')

// ---- 数据备份与还原 ----
const exporting = ref(false)
const backupBusy = ref(false)
const backupFile = ref<File | null>(null)
const backupPreview = ref<BackupPreview | null>(null)
const backupResult = ref<BackupImportResult | null>(null)
const backupError = ref('')
const fileInput = ref<HTMLInputElement | null>(null)

async function doExport() {
  exporting.value = true
  backupError.value = ''
  try {
    const resp = await backupApi.exportFile()
    const url = URL.createObjectURL(new Blob([resp.data], { type: 'application/zip' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `clashserver-backup-${dateStamp()}.zip`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  } catch (err) {
    backupError.value = (err as any)?.response?.data?.error ?? '导出失败'
  } finally {
    exporting.value = false
  }
}

function dateStamp() {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`
}

function pickFile(ev: Event) {
  const f = (ev.target as HTMLInputElement).files?.[0]
  backupFile.value = f ?? null
  backupPreview.value = null
  backupResult.value = null
  backupError.value = ''
  if (f) tryPreview(f)
}

async function tryPreview(f: File) {
  backupBusy.value = true
  backupError.value = ''
  try {
    const res = await backupApi.preview(f)
    if (!res.ok) backupError.value = res.error ?? '无法解析此备份文件'
    else backupPreview.value = res.data
  } catch (err) {
    backupError.value = (err as any)?.response?.data?.error ?? '导入失败'
  } finally {
    backupBusy.value = false
  }
}

async function confirmImport() {
  if (!backupFile.value) return
  backupBusy.value = true
  backupError.value = ''
  try {
    const res = await backupApi.importFile(backupFile.value)
    if (!res.ok) {
      backupError.value = res.error ?? '导入失败'
    } else {
      backupResult.value = res.data
      backupPreview.value = null
    }
  } catch (err) {
    backupError.value = (err as any)?.response?.data?.error ?? '导入失败'
  } finally {
    backupBusy.value = false
  }
}

function resetBackup() {
  backupFile.value = null
  backupPreview.value = null
  backupResult.value = null
  backupError.value = ''
  if (fileInput.value) fileInput.value.value = ''
}

onMounted(reload)
</script>

<style scoped>
.settings-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.setting-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px;
}

.loading-card {
  padding: 40px;
  text-align: center;
}

.section-title {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}
.title-icon {
  width: 36px;
  height: 36px;
  border-radius: 8px;
  background: rgba(47, 111, 237, 0.1);
  color: var(--brand);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.section-title h2 {
  margin: 0 0 2px 0;
  font-size: 17px;
  font-weight: 700;
}
.subtitle {
  font-size: 13px;
  margin: 0;
}

.section-subtitle {
  font-size: 14px;
  font-weight: 600;
  color: var(--text);
  margin-top: 4px;
}

.divider {
  height: 1px;
  background: var(--border);
  margin: 8px 0;
}

.setting-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.field label {
  font-size: 13px;
  font-weight: 600;
  color: var(--text);
}
.hint {
  color: var(--muted);
  font-size: 12px;
  margin: 0;
  line-height: 1.4;
}

.token-row {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.token-row input {
  flex: 1;
  min-width: 200px;
}

.mono {
  font-family: "JetBrains Mono", monospace;
}

.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
}

.pwd-grid {
  grid-template-columns: 1fr 1fr;
}

.switch-list {
  display: flex;
  flex-direction: column;
  background: var(--card-subtle);
  border: 1px solid var(--border);
  border-radius: 10px;
  overflow: hidden;
}

.switch-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 16px;
  border-bottom: 1px solid var(--border);
}
.switch-item:last-child {
  border-bottom: none;
}

.switch-info {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding-right: 12px;
}
.switch-title {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--text);
}
.switch-desc {
  font-size: 12px;
  color: var(--muted);
  line-height: 1.4;
}

.actions {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
  margin-top: 4px;
}

.save-btn {
  min-width: 110px;
}

.update-time {
  font-size: 12px;
  margin-left: auto;
}

.feedback-banner {
  padding: 10px 14px;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 500;
}
.error-banner {
  background: rgba(224, 49, 49, 0.08);
  color: var(--danger);
  border: 1px solid rgba(224, 49, 49, 0.2);
}
.success-banner {
  background: rgba(43, 138, 62, 0.08);
  color: var(--ok);
  border: 1px solid rgba(43, 138, 62, 0.2);
}

.restore-panel {
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 14px 16px;
  background: var(--card-subtle);
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.restore-title {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--text);
  margin: 0;
}
.restore-list {
  margin: 0;
  padding-left: 18px;
  font-size: 13px;
  color: var(--text);
}
.restore-list li {
  margin: 2px 0;
}
.restore-warn {
  color: var(--danger);
  font-size: 12.5px;
  margin: 0;
  line-height: 1.4;
}
.restore-panel .actions {
  margin-top: 4px;
}
.backup-result {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.backup-result .hint {
  padding-top: 4px;
}

@media (max-width: 768px) {
  .setting-section {
    padding: 16px;
  }
  .grid, .pwd-grid {
    grid-template-columns: 1fr;
    gap: 12px;
  }
  .token-row .btn {
    flex: 1 1 calc(50% - 6px);
  }
  .actions {
    flex-direction: column;
    align-items: stretch;
  }
  .actions .btn {
    width: 100%;
    min-height: 42px;
  }
  .update-time {
    margin-left: 0;
    text-align: center;
  }
}
</style>