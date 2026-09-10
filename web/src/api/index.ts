import http, { unwrap } from './http'
import type { ApiResponse, DashboardDto, RuleDto, SettingsDto, ConfigDto, ProxyNode } from '@/types'

export const authApi = {
  login: (username: string, password: string) =>
    unwrap(http.post<ApiResponse<unknown>>('/api/auth/login', { username, password })),
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
  moveUp: (id: string) => unwrap(http.post<ApiResponse<unknown>>(`/api/rules/${id}/move-up`)),
  moveDown: (id: string) => unwrap(http.post<ApiResponse<unknown>>(`/api/rules/${id}/move-down`)),
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