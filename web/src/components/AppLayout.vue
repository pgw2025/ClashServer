<template>
  <div class="layout">
    <header class="topbar">
      <div class="brand" @click="$router.push('/')">Clash Server</div>
      <nav class="nav">
        <router-link to="/">仪表盘</router-link>
        <router-link to="/rules">规则管理</router-link>
        <router-link to="/rules/import">批量导入</router-link>
        <router-link to="/config">配置对比</router-link>
        <router-link to="/settings">设置</router-link>
      </nav>
      <div class="right">
        <button class="btn" @click="onLogout">退出</button>
      </div>
    </header>
    <main class="content">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

async function onLogout() {
  await auth.logout()
  router.push('/login')
}
</script>

<style scoped>
.layout { min-height: 100vh; }
.topbar {
  display: flex; align-items: center; gap: 24px;
  background: var(--card); border-bottom: 1px solid var(--border);
  padding: 0 20px; height: 52px; position: sticky; top: 0; z-index: 10;
}
.brand { font-weight: 700; font-size: 16px; cursor: pointer; }
.nav { display: flex; gap: 16px; flex: 1; }
.nav a { color: var(--muted); padding: 4px 2px; }
.nav a.router-link-active { color: var(--brand); font-weight: 600; }
.content { padding: 20px; max-width: 1200px; margin: 0 auto; }
</style>