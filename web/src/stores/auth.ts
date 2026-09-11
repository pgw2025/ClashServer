import { defineStore } from 'pinia'
import { ref } from 'vue'
import { authApi } from '@/api'

export const useAuthStore = defineStore('auth', () => {
  const loggedIn = ref(false)
  const checked = ref(false)

  async function check() {
    try {
      const res = await authApi.status()
      loggedIn.value = res.data?.loggedIn ?? false
      if (!loggedIn.value) {
        localStorage.removeItem('clash_token')
      }
    } catch {
      loggedIn.value = false
    } finally {
      checked.value = true
    }
  }

  async function login(username: string, password: string) {
    const res = await authApi.login(username, password)
    const token = res.data?.token
    if (token) {
      localStorage.setItem('clash_token', token)
    }
    loggedIn.value = true
    checked.value = true
  }

  async function logout() {
    try {
      await authApi.logout()
    } finally {
      localStorage.removeItem('clash_token')
      loggedIn.value = false
    }
  }

  return { loggedIn, checked, check, login, logout }
})