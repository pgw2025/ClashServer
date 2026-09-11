<template>
  <div class="rules-view">
    <StaleBanner :is-stale="stale.isStale" :last-good-update="stale.lastGoodUpdate" />

    <div class="card toolbar-card">
      <div class="toolbar">
        <button class="btn primary" @click="openForm()">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <line x1="12" y1="5" x2="12" y2="19"></line>
            <line x1="5" y1="12" x2="19" y2="12"></line>
          </svg>
          <span>新增规则</span>
        </button>

        <div class="search-box grow">
          <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="11" cy="11" r="8"></circle>
            <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
          </svg>
          <input v-model.trim="search" class="search-input" placeholder="搜索匹配内容 / 备注…" />
        </div>
        
        <div class="batch-bar" v-if="rules.length">
          <span class="muted select-count">已选 {{ selected.size }} 项</span>
          <select v-model="targetPolicy" class="compact">
            <option value="">— 改策略为… —</option>
            <option v-for="p in allPolicies" :key="p" :value="p">{{ p }}</option>
          </select>
          <button class="btn" :disabled="selected.size === 0 || !targetPolicy" @click="applyBatchPolicy">批量改策略</button>
          <button class="btn danger" :disabled="selected.size === 0" @click="batchDelete">批量删除</button>
          <button class="btn danger" @click="clearAll">清空全部</button>
        </div>
      </div>
    </div>

    <!-- 桌面端表格视图 -->
    <div class="card desktop-table-wrapper">
      <table class="table">
        <thead>
          <tr>
            <th style="width: 36px;"><input type="checkbox" :checked="allSelected" @change="toggleAll" /></th>
            <th style="width: 60px;">状态</th>
            <th v-for="col in columns" :key="col.key" @click="toggleSort(col.key)" class="sortable">
              {{ col.label }}
              <span v-if="sortKey === col.key" class="sort-mark">{{ sortDir === 'asc' ? '↑' : '↓' }}</span>
            </th>
            <th style="width: 230px;">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in filteredRules" :key="r.id" class="rule-row" :class="{ 'row-disabled': !r.enabled }">
            <td><input type="checkbox" :checked="selected.has(r.id)" @change="toggleSelect(r.id)" /></td>
            <td>
              <label class="switch-toggle" title="切换启用状态">
                <input type="checkbox" :checked="r.enabled" @change="toggleRule(r.id)" />
                <span class="switch-slider"></span>
              </label>
            </td>
            <td><span class="badge-type">{{ r.ruleType }}</span></td>
            <td class="mono target-text" :title="r.target">{{ r.target }}</td>
            <td><span class="policy-pill" :class="policyClass(r.policy)">{{ r.policy }}</span></td>
            <td class="muted">{{ r.remark || '—' }}</td>
            <td class="muted text-time">{{ formatTime(r.updatedAt) }}</td>
            <td class="ops">
              <button class="btn mini" @click="move(r.id, 'top')" title="置顶">⏫</button>
              <button class="btn mini" @click="move(r.id, 'up')" title="上移">↑</button>
              <button class="btn mini" @click="move(r.id, 'down')" title="下移">↓</button>
              <button class="btn mini" @click="move(r.id, 'bottom')" title="置底">⏬</button>
              <button class="btn mini" @click="openForm(r)">编辑</button>
              <button class="btn mini danger" @click="remove(r)">删除</button>
            </td>
          </tr>
          <tr v-if="!filteredRules.length">
            <td colspan="8" class="muted center empty-cell">暂无匹配规则</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 移动端卡片流列表 -->
    <div class="mobile-rules-list">
      <div class="mobile-select-all" v-if="filteredRules.length">
        <label class="check-label">
          <input type="checkbox" :checked="allSelected" @change="toggleAll" />
          <span>全选当前 {{ filteredRules.length }} 条</span>
        </label>
      </div>

      <div v-for="r in filteredRules" :key="r.id" class="card rule-card" :class="{ 'card-disabled': !r.enabled }">
        <div class="rule-card-top">
          <label class="check-label">
            <input type="checkbox" :checked="selected.has(r.id)" @change="toggleSelect(r.id)" />
            <span class="badge-type">{{ r.ruleType }}</span>
          </label>
          <div class="rule-card-status">
            <label class="switch-toggle">
              <input type="checkbox" :checked="r.enabled" @change="toggleRule(r.id)" />
              <span class="switch-slider"></span>
            </label>
          </div>
        </div>

        <div class="rule-target mono">{{ r.target }}</div>

        <div class="rule-meta-row">
          <span class="policy-pill" :class="policyClass(r.policy)">{{ r.policy }}</span>
          <span v-if="r.remark" class="rule-remark muted">📝 {{ r.remark }}</span>
        </div>

        <div class="rule-card-actions">
          <div class="move-group">
            <button class="btn mini" @click="move(r.id, 'top')" title="置顶">⏫</button>
            <button class="btn mini" @click="move(r.id, 'up')" title="上移">↑</button>
            <button class="btn mini" @click="move(r.id, 'down')" title="下移">↓</button>
            <button class="btn mini" @click="move(r.id, 'bottom')" title="置底">⏬</button>
          </div>
          <div class="edit-group">
            <button class="btn mini" @click="openForm(r)">编辑</button>
            <button class="btn mini danger" @click="remove(r)">删除</button>
          </div>
        </div>
      </div>

      <div v-if="!filteredRules.length" class="card center muted py-6">
        暂无匹配规则
      </div>
    </div>

    <!-- 编辑/新增弹窗 -->
    <div v-if="showForm" class="overlay" @click.self="closeForm">
      <div class="card form">
        <h3>{{ editing ? '编辑路由规则' : '新增路由规则' }}</h3>
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
          <input v-model.trim="form.remark" placeholder="可选备注信息" />
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
const sortKey = ref<string>('')
const sortDir = ref<'asc' | 'desc' | ''>('')
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
  if (!sortKey.value) return arr
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

async function move(id: string, dir: 'top' | 'bottom' | 'up' | 'down') {
  if (dir === 'top') {
    const r = rules.value.find((x) => x.id === id)
    if (r?.ruleType === 'MATCH' && !confirm('MATCH 规则应放在列表末尾兜底，置顶后其后的规则将永远不会被匹配。仍要置顶吗？')) return
  }
  await rulesApi.move(id, dir)
  sortKey.value = ''
  sortDir.value = ''
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

function policyClass(p: string) {
  const up = (p || '').toUpperCase()
  if (up === 'DIRECT') return 'direct'
  if (up === 'PROXY') return 'proxy'
  if (up === 'REJECT') return 'reject'
  return 'custom'
}

onMounted(() => {
  load()
  dashboardApi.groups().then((g) => { groups.value = g.data ?? [] })
})
</script>

<style scoped>
.rules-view { display: flex; flex-direction: column; gap: 16px; }

.toolbar-card {
  padding: 12px 16px;
}
.toolbar { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
.grow { flex: 1; min-width: 200px; }

.search-box {
  position: relative;
  display: flex;
  align-items: center;
}
.search-icon {
  position: absolute;
  left: 10px;
  color: var(--muted);
  pointer-events: none;
}
.search-input {
  width: 100%;
  padding-left: 32px;
}

.batch-bar { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.compact { width: 140px; }
.select-count { white-space: nowrap; font-size: 13px; font-weight: 500; }
.sortable { cursor: pointer; user-select: none; white-space: nowrap; }
.sort-mark { color: var(--brand); font-weight: bold; margin-left: 2px; }

.mono { font-family: "JetBrains Mono", SFMono-Regular, Menlo, Monaco, Consolas, monospace; word-break: break-all; }
.target-text { font-size: 13px; color: var(--text); font-weight: 500; }
.text-time { font-size: 12px; }

.badge-type {
  font-size: 11px;
  font-weight: 600;
  padding: 2px 7px;
  background: var(--card-subtle);
  border: 1px solid var(--border);
  border-radius: 4px;
  color: var(--text-secondary);
}

.rule-row {
  transition: background 0.12s ease, opacity 0.15s ease;
}
.rule-row.row-disabled {
  opacity: 0.55;
  background: var(--bg);
}
.rule-row:hover:not(.row-disabled) {
  background: var(--card-subtle);
}

.ops { display: flex; gap: 4px; flex-wrap: wrap; }
.btn.mini { padding: 4px 8px; font-size: 12px; min-height: 28px; }
.center { text-align: center; }
.empty-cell { padding: 32px !important; }
.py-6 { padding-top: 24px; padding-bottom: 24px; }

.overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(4px);
  -webkit-backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 60;
  padding: 16px;
}
.form {
  width: 440px;
  max-width: 100%;
  display: flex;
  flex-direction: column;
  gap: 14px;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.2);
}
.form h3 {
  margin: 0 0 4px 0;
  font-size: 16px;
  font-weight: 700;
}
.form label { display: flex; flex-direction: column; gap: 5px; font-size: 13px; font-weight: 600; color: var(--text-secondary); }
.form-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 6px; }

/* Mobile Card View Rules */
.mobile-rules-list { display: none; flex-direction: column; gap: 10px; }
.mobile-select-all { padding: 4px 4px 8px; }
.check-label { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; user-select: none; }
.check-label input { width: 16px; height: 16px; margin: 0; }

.rule-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 14px;
  transition: opacity 0.15s ease;
}
.rule-card.card-disabled {
  opacity: 0.55;
  background: var(--bg);
}
.rule-card-top { display: flex; justify-content: space-between; align-items: center; }
.rule-target { font-size: 14px; font-weight: 600; color: var(--text); }
.rule-meta-row { display: flex; align-items: center; gap: 10px; font-size: 12px; flex-wrap: wrap; margin-top: 2px; }
.rule-remark { font-size: 12px; }
.rule-card-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-top: 1px solid var(--border);
  padding-top: 10px;
  margin-top: 4px;
}
.move-group, .edit-group { display: flex; gap: 6px; }

@media (max-width: 768px) {
  .desktop-table-wrapper { display: none; }
  .mobile-rules-list { display: flex; }
  .toolbar { gap: 8px; }
  .toolbar .grow { min-width: 100%; order: 1; }
  .toolbar > .btn.primary { width: 100%; order: 0; min-height: 40px; }
  .batch-bar { order: 2; width: 100%; justify-content: space-between; }
  .batch-bar .compact { flex: 1; min-width: 110px; }
  .batch-bar .btn { flex: 1; min-height: 36px; padding: 4px 6px; }
}
</style>