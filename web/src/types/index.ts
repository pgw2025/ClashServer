// 与后端 Models/Dtos.cs 对应的类型（JSON 字段同后端 camelCase）

export interface ApiResponse<T> {
  ok: boolean
  data: T | null
  error: string | null
  lastGoodUpdate: string | null
  isStale: boolean
}

export interface RuleDto {
  id: string
  ruleType: string
  target: string
  policy: string
  enabled: boolean
  remark: string | null
  updatedAt: string
}

export interface SettingsDto {
  upstreamUrl: string | null
  accessToken: string | null
  username: string | null
  cacheMinutes: number
  adminFetchTimeoutSeconds: number
  publicSubFetchTimeoutSeconds: number
  insertRulesBefore: boolean
  replaceMode: boolean
  autoGroupNodes: boolean
  updatedAt: string
}

export interface ProxyNode {
  name: string
  type: string
  server: string
  port: number
  latency: number | null
  error: string | null
}

export interface DashboardDto {
  subUrl: string
  enabledRuleCount: number
  nodeCount: number
  lastGoodUpdate: string | null
  isStale: boolean
}

export interface ConfigDto {
  yaml: string
  lineCount: number
  ruleCount: number
  lastGoodUpdate: string | null
  isStale: boolean
}