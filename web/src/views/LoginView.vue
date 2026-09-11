<template>
  <div class="login-wrap">
    <div class="card login-card">
      <h2>Clash Server 管理端</h2>
      <form @submit.prevent="onSubmit">
        <label>用户名</label>
        <input v-model.trim="username" type="text" autocomplete="username" placeholder="请输入用户名" required />
        <label>密码</label>
        <input v-model="password" type="password" autocomplete="current-password" placeholder="请输入密码" required />
        <button class="btn primary" :disabled="loading">{{ loading ? '登录中…' : '登录' }}</button>
      </form>
      <p v-if="error" class="danger-text">{{ error }}</p>
      <p class="hint-text" style="margin-top: 16px; font-size: 13px; color: #888; text-align: center;">
        默认管理员：<code>admin</code> / 密码：<code>admin123</code>
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

async function onSubmit() {
  error.value = ''
  loading.value = true
  try {
    await auth.login(username.value, password.value)
    let redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    if (!redirect || redirect.startsWith('/login')) {
      redirect = '/'
    }
    await router.replace(redirect)
  } catch (e: any) {
    error.value = e?.response?.data?.error ?? '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-wrap {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}
.login-card {
  width: 380px;
  max-width: 100%;
}
.login-card form {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 16px;
}
.login-card form label {
  font-weight: 500;
  font-size: 13.5px;
}
.login-card form input {
  min-height: 40px;
}
.login-card form .btn {
  min-height: 42px;
  margin-top: 6px;
  font-size: 15px;
  font-weight: 500;
}
.login-card h2 {
  margin: 0 0 8px;
  font-size: 18px;
  text-align: center;
}
</style>