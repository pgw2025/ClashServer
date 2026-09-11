<template>
  <div class="import-view">
    <StaleBanner :is-stale="stale.isStale" :last-good-update="stale.lastGoodUpdate" />

    <div class="card import-card">
      <div class="card-header">
        <div>
          <h2>批量导入规则</h2>
          <p class="muted subtitle">粘贴 Clash rules 规则段，或上传 .yaml / .yml / .txt 规则文件进行语法解析与批量入库。</p>
        </div>
      </div>

      <!-- 文件上传与快捷操作 -->
      <div class="upload-bar">
        <button class="btn" @click="fileInput?.click()">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="17 8 12 3 7 8"></polyline>
            <line x1="12" y1="3" x2="12" y2="15"></line>
          </svg>
          <span>{{ fileName ? '更换文件…' : '选择规则文件…' }}</span>
        </button>
        <input ref="fileInput" type="file" accept=".yaml,.yml,.txt" hidden @change="onFile" />
        
        <button class="btn primary parse-btn" :disabled="!yamlText.trim() || parsing" @click="preview">
          <svg v-if="!parsing" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>
          </svg>
          <span>{{ parsing ? '解析中…' : '解析预览' }}</span>
        </button>

        <span v-if="fileName" class="file-tag">
          📄 {{ fileName }}
          <button class="file-clear" @click="clearFile" title="清除文件">✕</button>
        </span>
      </div>

      <div class="editor-wrapper">
        <textarea
          v-model="yamlText"
          class="yaml-input mono"
          rows="11"
          placeholder="在此粘贴 Clash 规则段，例如：
  - DOMAIN-SUFFIX,google.com,PROXY
  - DOMAIN-KEYWORD,github,PROXY
  - IP-CIDR,192.168.0.0/16,DIRECT"
        ></textarea>
      </div>

      <div v-if="previewError" class="feedback-banner error-banner">
        ⚠️ {{ previewError }}
      </div>
      <div v-if="importSuccessMsg" class="feedback-banner success-banner">
        ✓ {{ importSuccessMsg }}
      </div>
    </div>

    <!-- 预览区域 -->
    <div v-if="parsed.length" class="card preview-card">
      <div class="preview-header">
        <div class="preview-title">
          <h3>解析结果预览</h3>
          <span class="count-pill">共 {{ parsed.length }} 条规则</span>
        </div>
        <button class="btn primary confirm-btn" :disabled="importing" @click="doImport">
          {{ importing ? '入库中…' : `确认导入 ${parsed.length} 条规则` }}
        </button>
      </div>

      <div class="table-responsive">
        <table class="table">
          <thead>
            <tr>
              <th style="width: 100px;">类型</th>
              <th>匹配内容</th>
              <th style="width: 140px;">目标策略</th>
              <th>备注</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(r, i) in parsed" :key="i">
              <td><span class="badge-type">{{ r.ruleType }}</span></td>
              <td class="mono target-cell">{{ r.target }}</td>
              <td><span class="policy-pill" :class="policyClass(r.policy)">{{ r.policy }}</span></td>
              <td class="muted">{{ r.remark || '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import StaleBanner from '@/components/StaleBanner.vue'
import { rulesApi, dashboardApi } from '@/api'
import type { RuleDto } from '@/types'

const router = useRouter()
const yamlText = ref('')
const fileName = ref('')
const fileInput = ref<HTMLInputElement | null>(null)
const parsed = ref<RuleDto[]>([])
const parsing = ref(false)
const importing = ref(false)
const previewError = ref('')
const importSuccessMsg = ref('')
const stale = ref<{ isStale: boolean; lastGoodUpdate: string | null }>({ isStale: false, lastGoodUpdate: null })

function policyClass(p: string) {
  const up = (p || '').toUpperCase()
  if (up === 'DIRECT') return 'direct'
  if (up === 'PROXY') return 'proxy'
  if (up === 'REJECT') return 'reject'
  return 'custom'
}

function clearFile() {
  fileName.value = ''
  if (fileInput.value) fileInput.value.value = ''
}

async function onFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return
  fileName.value = file.name
  yamlText.value = await file.text()
}

async function preview() {
  if (!yamlText.value.trim()) return
  parsing.value = true
  previewError.value = ''
  importSuccessMsg.value = ''
  try {
    const res = await rulesApi.parse(yamlText.value)
    if (!res.ok) {
      previewError.value = res.error ?? '解析失败，请检查规则格式'
    } else {
      parsed.value = res.data ?? []
      if (parsed.value.length === 0) {
        previewError.value = '未从内容中解析出有效规则'
      }
    }
  } catch (err) {
    previewError.value = (err as any)?.response?.data?.error ?? '解析失败，请检查语法'
  } finally {
    parsing.value = false
  }
}

async function doImport() {
  importing.value = true
  importSuccessMsg.value = ''
  try {
    await rulesApi.import(parsed.value)
    importSuccessMsg.value = `成功导入 ${parsed.value.length} 条规则，正在前往规则列表…`
    setTimeout(() => {
      router.push('/rules')
    }, 900)
  } catch (err) {
    previewError.value = (err as any)?.response?.data?.error ?? '导入失败'
  } finally {
    importing.value = false
  }
}

dashboardApi.get().then((d) => { stale.value = { isStale: d.isStale, lastGoodUpdate: d.lastGoodUpdate } })
</script>

<style scoped>
.import-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.import-card {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card-header h2 {
  margin: 0 0 4px 0;
  font-size: 18px;
  font-weight: 700;
}

.subtitle {
  font-size: 13px;
}

.upload-bar {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}

.file-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: var(--card-subtle);
  border: 1px solid var(--border);
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 12px;
  color: var(--text-secondary);
}

.file-clear {
  background: none;
  border: none;
  color: var(--muted);
  cursor: pointer;
  padding: 0 2px;
  font-size: 12px;
}
.file-clear:hover {
  color: var(--danger);
}

.editor-wrapper {
  border-radius: 8px;
  overflow: hidden;
}

.yaml-input {
  width: 100%;
  font-size: 13px;
  line-height: 1.6;
  box-sizing: border-box;
  resize: vertical;
  background: var(--bg);
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

.preview-card {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.preview-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.preview-title {
  display: flex;
  align-items: center;
  gap: 10px;
}

.preview-title h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
}

.count-pill {
  font-size: 12px;
  font-weight: 600;
  color: var(--brand);
  background: rgba(47, 111, 237, 0.08);
  padding: 2px 8px;
  border-radius: 12px;
}

.badge-type {
  font-size: 11px;
  font-weight: 600;
  padding: 2px 7px;
  background: var(--card-subtle);
  border: 1px solid var(--border);
  border-radius: 4px;
  color: var(--text-secondary);
}

.target-cell {
  font-size: 13px;
  font-weight: 500;
}

.table-responsive {
  width: 100%;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

@media (max-width: 768px) {
  .upload-bar {
    width: 100%;
  }
  .upload-bar .btn {
    flex: 1 1 calc(50% - 6px);
  }
  .preview-header {
    flex-direction: column;
    align-items: stretch;
  }
  .preview-header .confirm-btn {
    width: 100%;
  }
}
</style>