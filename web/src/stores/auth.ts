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
    } catch {
      loggedIn.value = false
    } finally {
      checked.value = true
    }
  }

  async function login(username: string, password: string) {
    await authApi.login(username, password)
    loggedIn.value = true
  }

  async function logout() {
    try {
      await authApi.logout()
    } finally {
      loggedIn.value = false
    }
  }

  return { loggedIn, checked, check, login, logout }
})