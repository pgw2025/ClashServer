import axios from 'axios'
import type { ApiResponse } from '@/types'

// 同源 axios 实例：建 Token 时统一带上 Cookie（credentials 默认 same-origin 即可）
const http = axios.create({
  baseURL: '',
  headers: { 'Content-Type': 'application/json' }
})

// 写操作统一注入 X-Requested-With 自定义头，配合后端 CSRF 防线；同时附带 Bearer token 应对跨域 iframe Cookie 限制
http.interceptors.request.use((config) => {
  const method = (config.method ?? 'get').toLowerCase()
  if (method !== 'get' && method !== 'head' && method !== 'options') {
    config.headers['X-Requested-With'] = 'fetch'
  }
  const token = localStorage.getItem('clash_token')
  if (token) {
    config.headers['Authorization'] = `Bearer ${token}`
  }
  return config
})

// 401 → 清除本地 token，跳到登录页并携带 redirect 回跳参数
http.interceptors.response.use(
  (resp) => resp,
  (error) => {
    if (error?.response?.status === 401) {
      localStorage.removeItem('clash_token')
      const redirect = encodeURIComponent(window.location.pathname + window.location.search)
      if (window.location.pathname !== '/login') {
        window.location.href = `/login?redirect=${redirect}`
      }
    }
    return Promise.reject(error)
  }
)

// 解析统一响应壳，失败抛出后端 error 文案
export async function unwrap<T>(p: Promise<{ data: ApiResponse<T> }>): Promise<ApiResponse<T>> {
  const resp = await p
  return resp.data
}

export default http