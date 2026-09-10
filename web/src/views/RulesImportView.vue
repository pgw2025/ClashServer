<template>
  <div>
    <StaleBanner :is-stale="stale.isStale" :last-good-update="stale.lastGoodUpdate" />

    <div class="card">
      <h2>批量导入规则</h2>
      <p class="muted">粘贴 Clash 规则 YAML（rules 段），或上传 .yaml/.yml/.txt 文件，预览后确认导入。</p>

      <div class="row">
        <button class="btn" @click="fileInput?.click()">选择文件…</button>
        <input ref="fileInput" type="file" accept=".yaml,.yml,.txt" hidden @change="onFile" />
        <button class="btn primary" :disabled="!yamlText.trim() || parsing" @click="preview">
          {{ parsing ? '解析中…' : '解析预览' }}
        </button>
        <span v-if="fileName" class="muted">{{ fileName }}</span>
      </div>

      <textarea v-model="yamlText" class="yaml-input mono" rows="10" placeholder="在此粘贴 YAML 规则内容…"></textarea>
      <p v-if="previewError" class="danger-text">{{ previewError }}</p>
    </div>

    <div v-if="parsed.length" class="card">
      <div class="row" style="justify-content: space-between;">
        <h3>预览（{{ parsed.length }} 条）</h3>
        <button class="btn primary" :disabled="importing" @click="doImport">
          {{ importing ? '导入中…' : `确认导入 ${parsed.length} 条` }}
        </button>
      </div>
      <table class="table">
        <thead>
          <tr><th>类型</th><th>匹配内容</th><th>策略</th><th>备注</th></tr>
        </thead>
        <tbody>
          <tr v-for="(r, i) in parsed" :key="i">
            <td>{{ r.ruleType }}</td>
            <td class="mono">{{ r.target }}</td>
            <td>{{ r.policy }}</td>
            <td class="muted">{{ r.remark || '—' }}</td>
          </tr>
        </tbody>
      </table>
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
const stale = ref<{ isStale: boolean; lastGoodUpdate: string | null }>({ isStale: false, lastGoodUpdate: null })

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
  try {
    const res = await rulesApi.parse(yamlText.value)
    if (!res.ok) { previewError.value = res.error ?? '解析失败' }
    else parsed.value = res.data ?? []
  } catch (err) {
    previewError.value = (err as any)?.response?.data?.error ?? '解析失败'
  } finally {
    parsing.value = false
  }
}

async function doImport() {
  importing.value = true
  try {
    await rulesApi.import(parsed.value)
    alert(`成功导入 ${parsed.value.length} 条规则`)
    router.push('/rules')
  } catch (err) {
    alert((err as any)?.response?.data?.error ?? '导入失败')
  } finally {
    importing.value = false
  }
}

dashboardApi.get().then((d) => { stale.value = { isStale: d.isStale, lastGoodUpdate: d.lastGoodUpdate } })
</script>

<style scoped>
.row { display: flex; gap: 8px; align-items: center; margin-bottom: 12px; }
.yaml-input { width: 100%; font-size: 13px; line-height: 1.6; }
.mono { font-family: monospace; }
</style>