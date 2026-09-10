<template>
  <div>
    <StaleBanner :is-stale="stale.isStale" :last-good-update="stale.lastGoodUpdate" />

    <div class="toolbar">
      <button class="btn primary" @click="openForm()">新增规则</button>
      <input v-model.trim="search" class="grow" placeholder="搜索匹配内容 / 备注…" />
      <span class="muted">已选 {{ selected.size }} 项</span>
      <select v-model="targetPolicy" class="compact">
        <option value="">— 改策略为… —</option>
        <option v-for="p in allPolicies" :key="p" :value="p">{{ p }}</option>
      </select>
      <button class="btn" :disabled="selected.size === 0 || !targetPolicy" @click="applyBatchPolicy">批量改策略</button>
      <button class="btn danger" :disabled="selected.size === 0" @click="batchDelete">批量删除</button>
      <button class="btn danger" v-if="rules.length" @click="clearAll">清空全部</button>
    </div>

    <div class="card">
      <table class="table">
        <thead>
          <tr>
            <th><input type="checkbox" :checked="allSelected" @change="toggleAll" /></th>
            <th v-for="col in columns" :key="col.key" @click="toggleSort(col.key)" class="sortable">
              {{ col.label }}
              <span v-if="sortKey === col.key" class="muted">{{ sortDir === 'asc' ? '▲' : '▼' }}</span>
            </th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in filteredRules" :key="r.id">
            <td><input type="checkbox" :checked="selected.has(r.id)" @change="toggleSelect(r.id)" /></td>
            <td><span class="badge" :class="r.enabled ? 'ok' : 'muted-badge'">{{ r.enabled ? '启用' : '停用' }}</span></td>
            <td>{{ r.ruleType }}</td>
            <td class="mono">{{ r.target }}</td>
            <td>{{ r.policy }}</td>
            <td class="muted">{{ r.remark || '—' }}</td>
            <td class="muted">{{ formatTime(r.updatedAt) }}</td>
            <td class="ops">
              <button class="btn mini" @click="toggleRule(r.id)">{{ r.enabled ? '停用' : '启用' }}</button>
              <button class="btn mini" @click="move(r.id, 'up')">↑</button>
              <button class="btn mini" @click="move(r.id, 'down')">↓</button>
              <button class="btn mini" @click="openForm(r)">编辑</button>
              <button class="btn mini danger" @click="remove(r)">删除</button>
            </td>
          </tr>
          <tr v-if="!filteredRules.length">
            <td colspan="7" class="muted center">暂无规则</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="showForm" class="overlay" @click.self="closeForm">
      <div class="card form">
        <h3>{{ editing ? '编辑规则' : '新增规则' }}</h3>
        <label>规则类型
          <select v-model="form.ruleType">
            <option v-for="t in RULE_TYPES" :key="t" :value="t">{{ t }}</option>
          </select>
        </label>
        <label>匹配内容
          <input v-model.trim="form.target" placeholder="例如 example.com / 1.2.3.4" />
        </label>
        <label>策略
          <select v-model="form.policy">
            <option v-for="p in allPolicies" :key="p" :value="p">{{ p }}</option>
          </select>
        </label>
        <label>备注
          <input v-model.trim="form.remark" placeholder="可选" />
        </label>
        <p v-if="formError" class="danger-text">{{ formError }}</p>
        <div class="form-actions">
          <button class="btn" @click="closeForm">取消</button>
          <button class="btn primary" :disabled="saving" @click="save">{{ saving ? '保存中…' : '保存' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, reactive } from 'vue'
import StaleBanner from '@/components/StaleBanner.vue'
import { rulesApi, dashboardApi } from '@/api'
import type { RuleDto } from '@/types'

// 与后端 CustomRule.AvailableRuleTypes / AvailablePolicies 对应
const RULE_TYPES = [
  'DOMAIN', 'DOMAIN-SUFFIX', 'DOMAIN-KEYWORD', 'GEOIP', 'IP-CIDR', 'IP-CIDR6',
  'IP-ASN', 'SRC-IP-CIDR', 'SRC-PORT', 'DST-PORT', 'PROCESS-NAME', 'PROCESS-PATH',
  'NETWORK', 'UID', 'IN-PORT', 'IN-TYPE', 'IN-USER', 'IN-NAME', 'DSCP',
  'RULE-SET', 'GEOSITE', 'GEODATA', 'MATCH'
]
const BASE_POLICIES = ['DIRECT', 'REJECT', 'PROXY', 'REJECT-DROP', 'PASS', 'no-resolve']

const rules = ref<RuleDto[]>([])
const groups = ref<string[]>([])
const stale = ref<{ isStale: boolean; lastGoodUpdate: string | null }>({ isStale: false, lastGoodUpdate: null })
const search = ref('')
const sortKey = ref<string>('updatedAt')
const sortDir = ref<'asc' | 'desc'>('desc')
const selected = ref<Set<string>>(new Set())
const saving = ref(false)
const formError = ref('')

const columns = [
  { key: 'enabled', label: '状态' },
  { key: 'ruleType', label: '类型' },
  { key: 'target', label: '匹配内容' },
  { key: 'policy', label: '策略' },
  { key: 'remark', label: '备注' },
  { key: 'updatedAt', label: '更新时间' }
]

const allPolicies = computed(() => [...new Set([...groups.value, ...BASE_POLICIES])])

const showForm = ref(false)
const editing = ref<RuleDto | null>(null)
const form = reactive({ ruleType: 'DOMAIN', target: '', policy: 'PROXY', remark: '' })

async function load() {
  const [r, g] = await Promise.all([rulesApi.list(), dashboardApi.groups()])
  stale.value = { isStale: r.isStale, lastGoodUpdate: r.lastGoodUpdate }
  rules.value = r.data ?? []
  groups.value = g.data ?? []
}

const filteredRules = computed(() => {
  const kw = search.value.toLowerCase()
  const arr = rules.value.filter(
    (r) => !kw || (r.target + (r.remark ?? '')).toLowerCase().includes(kw)
  )
  const dir = sortDir.value === 'asc' ? 1 : -1
  arr.sort((a, b) => {
    const ka = a[sortKey.value as keyof RuleDto]
    const kb = b[sortKey.value as keyof RuleDto]
    if (ka == null) return 1
    if (kb == null) return -1
    if (typeof ka === 'boolean') return (ka === kb ? 0 : ka ? -1 : 1) * dir
    return String(ka).localeCompare(String(kb)) * dir
  })
  return arr
})

function toggleSort(key: string) {
  if (sortKey.value === key) sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
  else { sortKey.value = key; sortDir.value = 'asc' }
}

const allSelected = computed(() => rules.value.length > 0 && filteredRules.value.every((r) => selected.value.has(r.id)))
function toggleAll(e: Event) {
  const checked = (e.target as HTMLInputElement).checked
  if (checked) filteredRules.value.forEach((r) => selected.value.add(r.id))
  else selected.value.clear()
}
function toggleSelect(id: string) {
  const s = new Set(selected.value)
  if (!s.delete(id)) s.add(id)
  selected.value = s
}
function refreshSelected() {
  selected.value = new Set([...selected.value].filter((id) => rules.value.some((r) => r.id === id)))
}

function openForm(r?: RuleDto) {
  editing.value = r ?? null
  form.ruleType = r?.ruleType ?? 'DOMAIN'
  form.target = r?.target ?? ''
  form.policy = r?.policy ?? 'PROXY'
  form.remark = r?.remark ?? ''
  formError.value = ''
  showForm.value = true
}
function closeForm() { showForm.value = false }

async function save() {
  if (!form.target) { formError.value = '匹配内容不能为空'; return }
  formError.value = ''
  saving.value = true
  try {
    if (editing.value) {
      await rulesApi.update(editing.value.id, { ...form })
    } else {
      await rulesApi.create({ ...form, enabled: true })
    }
    closeForm()
    await load()
  } catch (e) {
    formError.value = (e as any)?.response?.data?.error ?? '保存失败'
  } finally {
    saving.value = false
  }
}

async function toggleRule(id: string) {
  await rulesApi.toggle(id)
  await load()
}

async function move(id: string, dir: 'up' | 'down') {
  dir === 'up' ? await rulesApi.moveUp(id) : await rulesApi.moveDown(id)
  await load()
}

async function remove(r: RuleDto) {
  if (!confirm(`确认删除规则「${r.target}」？`)) return
  await rulesApi.remove(r.id)
  await load()
}

async function applyBatchPolicy() {
  if (!targetPolicy.value) return
  if (!confirm(`确认将 ${selected.value.size} 条规则策略改为「${targetPolicy.value}」？`)) return
  await rulesApi.batch([...selected.value], targetPolicy.value)
  selected.value.clear()
  await load()
}

async function batchDelete() {
  if (!confirm(`确认删除选中的 ${selected.value.size} 条规则？`)) return
  for (const id of selected.value) await rulesApi.remove(id)
  selected.value.clear()
  await load()
}

async function clearAll() {
  if (!confirm(`确认清空全部 ${rules.value.length} 条规则？此操作不可恢复。`)) return
  await rulesApi.clear()
  await load()
}

const targetPolicy = ref('')
const formatTime = (iso: string) => new Date(iso).toLocaleString('zh-CN')

onMounted(() => {
  load()
  dashboardApi.groups().then((g) => { groups.value = g.data ?? [] })
})
</script>

<style scoped>
.toolbar { display: flex; gap: 8px; align-items: center; margin-bottom: 12px; flex-wrap: wrap; }
.grow { flex: 1; min-width: 180px; }
.compact { width: 160px; }
.sortable { cursor: pointer; user-select: none; white-space: nowrap; }
.badge { font-size: 11px; padding: 2px 8px; border-radius: 10px; border: 1px solid var(--border); }
.badge.ok { background: #e8f5e9; color: var(--ok); border-color: var(--ok); }
.muted-badge { background: var(--bg); color: var(--muted); }
.mono { font-family: monospace; word-break: break-all; }
.ops { display: flex; gap: 4px; flex-wrap: wrap; }
.btn.mini { padding: 2px 8px; font-size: 12px; }
.center { text-align: center; }
.overlay { position: fixed; inset: 0; background: #0009; display: flex; align-items: center; justify-content: center; z-index: 20; }
.form { width: 380px; max-width: 92vw; display: flex; flex-direction: column; gap: 12px; }
.form label { display: flex; flex-direction: column; gap: 4px; font-size: 13px; }
.form-actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>