<template>
  <div class="login-wrap">
    <div class="card login-card">
      <div class="login-header">
        <div class="brand-avatar">
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polygon points="12 2 2 7 12 12 22 7 12 2"></polygon>
            <polyline points="2 17 12 22 22 17"></polyline>
            <polyline points="2 12 12 17 22 12"></polyline>
          </svg>
        </div>
        <h2>Clash Rule Server</h2>
        <p class="muted subtitle">订阅聚合与自定义路由规则管理控制台</p>
      </div>

      <form @submit.prevent="onSubmit" class="login-form">
        <div class="field">
          <label>管理员账号</label>
          <div class="input-wrap">
            <svg class="input-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
              <circle cx="12" cy="7" r="4"></circle>
            </svg>
            <input v-model.trim="username" type="text" autocomplete="username" placeholder="请输入用户名" required />
          </div>
        </div>

        <div class="field">
          <label>管理密码</label>
          <div class="input-wrap">
            <svg class="input-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
              <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
            </svg>
            <input v-model="password" type="password" autocomplete="current-password" placeholder="请输入密码" required />
          </div>
        </div>

        <div v-if="error" class="feedback-banner error-banner">
          ⚠️ {{ error }}
        </div>

        <button type="submit" class="btn primary submit-btn" :disabled="loading">
          <span v-if="loading" class="spinner-small"></span>
          <span>{{ loading ? '正在验证…' : '登录控制台' }}</span>
        </button>
      </form>

      <div class="hint-box">
        <div class="hint-text">
          <span>默认管理员：<code>admin</code> / 密码：<code>admin123</code></span>
        </div>
        <button type="button" class="btn mini fill-btn" @click="fillDefaults">一键填入</button>
      </div>
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

function fillDefaults() {
  username.value = 'admin'
  password.value = 'admin123'
}

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
    error.value = e?.response?.data?.error ?? '登录失败，请检查账号密码'
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
  padding: 20px;
  background: radial-gradient(circle at 50% 20%, var(--card-subtle) 0%, var(--bg) 80%);
}

.login-card {
  width: 400px;
  max-width: 100%;
  padding: 32px 28px;
  box-shadow: 0 12px 32px -4px rgba(0, 0, 0, 0.08);
  border-radius: 14px;
}

.login-header {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  margin-bottom: 24px;
}

.brand-avatar {
  width: 52px;
  height: 52px;
  border-radius: 12px;
  background: var(--brand);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 12px;
  box-shadow: 0 4px 12px rgba(47, 111, 237, 0.3);
}

.login-header h2 {
  margin: 0 0 6px 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text);
}

.subtitle {
  font-size: 13px;
  margin: 0;
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.field label {
  font-weight: 600;
  font-size: 13px;
  color: var(--text);
}

.input-wrap {
  position: relative;
  display: flex;
  align-items: center;
}
.input-icon {
  position: absolute;
  left: 12px;
  color: var(--muted);
  pointer-events: none;
}
.input-wrap input {
  width: 100%;
  min-height: 42px;
  padding-left: 36px;
}

.submit-btn {
  min-height: 44px;
  font-size: 14.5px;
  font-weight: 600;
  margin-top: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.spinner-small {
  width: 14px;
  height: 14px;
  border: 2px solid #ffffff66;
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin {
  100% { transform: rotate(360deg); }
}

.feedback-banner {
  padding: 10px 14px;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 500;
}
.error-banner {
  background: rgba(224, 49, 49, 0.08);
  color: var(--danger);
  border: 1px solid rgba(224, 49, 49, 0.2);
}

.hint-box {
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
}
.hint-text code {
  background: var(--card-subtle);
  padding: 2px 5px;
  border-radius: 4px;
  font-size: 12px;
  color: var(--brand);
}

.fill-btn {
  font-size: 11px;
  padding: 3px 8px;
}
</style>