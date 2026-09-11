import http, { unwrap } from './http'
import type { ApiResponse, DashboardDto, RuleDto, SettingsDto, ConfigDto, ProxyNode, BackupPreview, BackupImportResult } from '@/types'

export const authApi = {
  login: (username: string, password: string) =>
    unwrap(http.post<ApiResponse<{ loggedIn: boolean; token?: string }>>('/api/auth/login', { username, password })),
  logout: () => unwrap(http.post<ApiResponse<unknown>>('/api/auth/logout')),
  status: () => unwrap(http.get<ApiResponse<{ loggedIn: boolean }>>('/api/auth/status'))
}

export const dashboardApi = {
  get: () => unwrap(http.get<ApiResponse<DashboardDto>>('/api/dashboard')),
  nodes: () => unwrap(http.get<ApiResponse<ProxyNode[]>>('/api/nodes')),
  groups: () => unwrap(http.get<ApiResponse<string[]>>('/api/groups')),
  latency: (name: string) =>
    unwrap(http.post<ApiResponse<{ ok: boolean; name: string; latency: number | null; error: string | null }>>('/api/nodes/latency', { name })),
  refresh: () => unwrap(http.post<ApiResponse<{ refreshed: boolean }>>('/api/cache/refresh')),
  health: () => http.get<{ ok: boolean }>('/api/sub-health')
}

export const rulesApi = {
  list: () => unwrap(http.get<ApiResponse<RuleDto[]>>('/api/rules')),
  create: (dto: Partial<RuleDto>) => unwrap(http.post<ApiResponse<RuleDto>>('/api/rules', dto)),
  update: (id: string, dto: Partial<RuleDto>) =>
    unwrap(http.put<ApiResponse<RuleDto>>(`/api/rules/${id}`, dto)),
  remove: (id: string) => unwrap(http.delete<ApiResponse<unknown>>(`/api/rules/${id}`)),
  toggle: (id: string) => unwrap(http.post<ApiResponse<RuleDto>>(`/api/rules/${id}/toggle`)),
  move: (id: string, direction: 'top' | 'bottom' | 'up' | 'down') =>
    unwrap(http.post<ApiResponse<unknown>>(`/api/rules/${id}/move?direction=${direction}`)),
  batch: (ids: string[], policy: string) =>
    unwrap(http.post<ApiResponse<unknown>>('/api/rules/batch', { ids, policy })),
  clear: () => unwrap(http.post<ApiResponse<unknown>>('/api/rules/clear')),
  parse: (yaml: string) => unwrap(http.post<ApiResponse<RuleDto[]>>('/api/rules/parse', { yaml })),
  import: (rules: RuleDto[]) => unwrap(http.post<ApiResponse<{ imported: number }>>('/api/rules/import', { rules }))
}

export const settingsApi = {
  get: () => unwrap(http.get<ApiResponse<SettingsDto>>('/api/settings')),
  save: (dto: Partial<SettingsDto>) => unwrap(http.put<ApiResponse<SettingsDto>>('/api/settings', dto)),
  changePassword: (currentPassword: string, newPassword: string) =>
    unwrap(http.post<ApiResponse<unknown>>('/api/settings/password', { currentPassword, newPassword })),
  generateToken: () => unwrap(http.post<ApiResponse<{ token: string }>>('/api/settings/generate-token'))
}

export const configApi = {
  raw: () => unwrap(http.get<ApiResponse<ConfigDto>>('/api/config/raw')),
  merged: () => unwrap(http.get<ApiResponse<ConfigDto>>('/api/config/merged')),
  refresh: () => unwrap(http.post<ApiResponse<{ refreshed: boolean }>>('/api/config/refresh'))
}

export const backupApi = {
  // 导出走原生 blob 下载，不走统一响应壳
  exportFile: () => http.get('/api/backup/export', { responseType: 'blob' }),
  // 上传必须显式声明 multipart/form-data，否则 axios 默认头 application/json 会把 FormData JSON 化（{file:{}}）
  preview: (file: File) => {
    const fd = new FormData()
    fd.append('file', file)
    return unwrap(http.post<ApiResponse<BackupPreview>>('/api/backup/preview', fd, { headers: { 'Content-Type': 'multipart/form-data' } }))
  },
  importFile: (file: File) => {
    const fd = new FormData()
    fd.append('file', file)
    return unwrap(http.post<ApiResponse<BackupImportResult>>('/api/backup/import', fd, { headers: { 'Content-Type': 'multipart/form-data' } }))
  }
}