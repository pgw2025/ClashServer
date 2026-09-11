<template>
  <div class="layout">
    <header class="topbar">
      <div class="brand-row">
        <button class="mobile-toggle" @click="mobileMenuOpen = !mobileMenuOpen" aria-label="切换菜单">
          <span class="bar"></span>
          <span class="bar"></span>
          <span class="bar"></span>
        </button>
        <div class="brand" @click="$router.push('/')">Clash Server</div>
      </div>

      <nav class="nav desktop-nav">
        <router-link to="/">仪表盘</router-link>
        <router-link to="/rules">规则管理</router-link>
        <router-link to="/rules/import">批量导入</router-link>
        <router-link to="/config">配置对比</router-link>
        <router-link to="/settings">设置</router-link>
      </nav>

      <div class="right desktop-right">
        <button class="btn" @click="onLogout">退出</button>
      </div>
    </header>

    <!-- 移动端侧边抽屉 / 遮罩 -->
    <div v-if="mobileMenuOpen" class="drawer-overlay" @click="mobileMenuOpen = false"></div>
    <aside class="mobile-drawer" :class="{ open: mobileMenuOpen }">
      <div class="drawer-head">
        <span class="drawer-title">Clash Server</span>
        <button class="close-btn" @click="mobileMenuOpen = false">✕</button>
      </div>
      <nav class="drawer-nav">
        <router-link to="/" @click="mobileMenuOpen = false">📊 仪表盘</router-link>
        <router-link to="/rules" @click="mobileMenuOpen = false">⚡ 规则管理</router-link>
        <router-link to="/rules/import" @click="mobileMenuOpen = false">📥 批量导入</router-link>
        <router-link to="/config" @click="mobileMenuOpen = false">📑 配置对比</router-link>
        <router-link to="/settings" @click="mobileMenuOpen = false">⚙️ 系统设置</router-link>
      </nav>
      <div class="drawer-foot">
        <button class="btn danger full-width" @click="onLogout">退出登录</button>
      </div>
    </aside>

    <main class="content">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const mobileMenuOpen = ref(false)

watch(() => route.path, () => {
  mobileMenuOpen.value = false
})

async function onLogout() {
  await auth.logout()
  router.push('/login')
}
</script>

<style scoped>
.layout { min-height: 100vh; display: flex; flex-direction: column; }
.topbar {
  display: flex; align-items: center; justify-content: space-between; gap: 16px;
  background: var(--card); border-bottom: 1px solid var(--border);
  padding: 0 20px; height: 52px; position: sticky; top: 0; z-index: 50;
}
.brand-row { display: flex; align-items: center; gap: 12px; }
.brand { font-weight: 700; font-size: 16px; cursor: pointer; user-select: none; }
.nav { display: flex; gap: 16px; flex: 1; }
.nav a { color: var(--muted); padding: 6px 4px; border-radius: 4px; }
.nav a.router-link-active { color: var(--brand); font-weight: 600; }
.content { padding: 20px; max-width: 1200px; margin: 0 auto; width: 100%; box-sizing: border-box; }

.mobile-toggle {
  display: none;
  background: none;
  border: none;
  cursor: pointer;
  padding: 8px 4px;
  flex-direction: column;
  gap: 4px;
}
.mobile-toggle .bar {
  display: block;
  width: 20px;
  height: 2px;
  background: var(--text);
  border-radius: 2px;
}

.drawer-overlay {
  display: none;
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  z-index: 90;
}

.mobile-drawer {
  display: none;
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  width: 260px;
  max-width: 80vw;
  background: var(--card);
  z-index: 100;
  flex-direction: column;
  border-right: 1px solid var(--border);
  box-shadow: 4px 0 16px rgba(0, 0, 0, 0.1);
  transform: translateX(-100%);
  transition: transform 0.25s ease-in-out;
}

.mobile-drawer.open {
  transform: translateX(0);
}

.drawer-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px;
  border-bottom: 1px solid var(--border);
}
.drawer-title { font-weight: 700; font-size: 16px; }
.close-btn {
  background: none;
  border: none;
  font-size: 18px;
  color: var(--muted);
  cursor: pointer;
  padding: 4px 8px;
}
.drawer-nav {
  display: flex;
  flex-direction: column;
  padding: 12px 8px;
  gap: 4px;
  flex: 1;
}
.drawer-nav a {
  padding: 12px 14px;
  color: var(--text);
  border-radius: 6px;
  font-size: 15px;
  font-weight: 500;
}
.drawer-nav a.router-link-active {
  background: rgba(47, 111, 237, 0.1);
  color: var(--brand);
  font-weight: 600;
}
.drawer-foot {
  padding: 16px;
  border-top: 1px solid var(--border);
}
.full-width { width: 100%; }

@media (max-width: 768px) {
  .topbar { padding: 0 16px; height: 50px; }
  .desktop-nav, .desktop-right { display: none; }
  .mobile-toggle { display: flex; }
  .drawer-overlay, .mobile-drawer { display: flex; }
  .content { padding: 12px 12px 24px; }
}
</style>