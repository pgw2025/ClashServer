<template>
  <div class="login-wrap">
    <div class="card login-card">
      <h2>Clash Server 管理端</h2>
      <form @submit.prevent="onSubmit">
        <label>访问 Token</label>
        <input v-model="token" type="password" autocomplete="current-password" placeholder="请输入 accessToken" required />
        <button class="btn primary" :disabled="loading">{{ loading ? '登录中…' : '登录' }}</button>
      </form>
      <p v-if="error" class="danger-text">{{ error }}</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const token = ref('')
const loading = ref(false)
const error = ref('')
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

async function onSubmit() {
  error.value = ''
  loading.value = true
  try {
    await auth.login(token.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    router.push(redirect)
  } catch (e: any) {
    error.value = e?.response?.data?.error ?? '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; }
.login-card { width: 360px; }
.login-card form { display: flex; flex-direction: column; gap: 10px; margin-top: 16px; }
.login-card h2 { margin: 0 0 8px; font-size: 18px; }
</style>