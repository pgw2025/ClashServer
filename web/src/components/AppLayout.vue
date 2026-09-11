<template>
  <div class="layout">
    <header class="topbar">
      <div class="brand-row">
        <button class="mobile-toggle" @click="mobileMenuOpen = !mobileMenuOpen" aria-label="切换菜单">
          <span class="bar"></span>
          <span class="bar"></span>
          <span class="bar"></span>
        </button>
        <div class="brand" @click="$router.push('/')">
          <div class="brand-icon">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
              <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
            </svg>
          </div>
          <span class="brand-text">Clash Server</span>
          <span class="brand-badge">HUB</span>
        </div>
      </div>

      <nav class="nav desktop-nav">
        <router-link to="/" class="nav-item">
          <span>仪表盘</span>
        </router-link>
        <router-link to="/rules" class="nav-item">
          <span>规则管理</span>
        </router-link>
        <router-link to="/rules/import" class="nav-item">
          <span>批量导入</span>
        </router-link>
        <router-link to="/config" class="nav-item">
          <span>配置对比</span>
        </router-link>
        <router-link to="/settings" class="nav-item">
          <span>系统设置</span>
        </router-link>
      </nav>

      <div class="right desktop-right">
        <button class="btn logout-btn" @click="onLogout">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path>
            <polyline points="16 17 21 12 16 7"></polyline>
            <line x1="21" y1="12" x2="9" y2="12"></line>
          </svg>
          <span>退出</span>
        </button>
      </div>
    </header>

    <!-- 移动端侧边抽屉 / 遮罩 -->
    <div v-if="mobileMenuOpen" class="drawer-overlay" @click="mobileMenuOpen = false"></div>
    <aside class="mobile-drawer" :class="{ open: mobileMenuOpen }">
      <div class="drawer-head">
        <div class="drawer-brand">
          <div class="brand-icon small">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
              <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
            </svg>
          </div>
          <span class="drawer-title">Clash Server</span>
        </div>
        <button class="close-btn" @click="mobileMenuOpen = false">✕</button>
      </div>
      <nav class="drawer-nav">
        <router-link to="/" @click="mobileMenuOpen = false">
          <span class="d-icon">📊</span> 仪表盘
        </router-link>
        <router-link to="/rules" @click="mobileMenuOpen = false">
          <span class="d-icon">⚡</span> 规则管理
        </router-link>
        <router-link to="/rules/import" @click="mobileMenuOpen = false">
          <span class="d-icon">📥</span> 批量导入
        </router-link>
        <router-link to="/config" @click="mobileMenuOpen = false">
          <span class="d-icon">📑</span> 配置对比
        </router-link>
        <router-link to="/settings" @click="mobileMenuOpen = false">
          <span class="d-icon">⚙️</span> 系统设置
        </router-link>
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
  display: flex; align-items: center; justify-content: space-between; gap: 20px;
  background: rgba(255, 255, 255, 0.82);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--border);
  padding: 0 24px; height: 56px; position: sticky; top: 0; z-index: 50;
  box-shadow: 0 1px 2px 0 rgba(0, 0, 0, 0.03);
}
.brand-row { display: flex; align-items: center; gap: 12px; }
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  user-select: none;
}
.brand-icon {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  background: linear-gradient(135deg, #2563eb, #1d4ed8);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 2px 4px rgba(37, 99, 235, 0.25);
}
.brand-icon.small {
  width: 28px;
  height: 28px;
  border-radius: 6px;
}
.brand-text {
  font-weight: 700;
  font-size: 16px;
  letter-spacing: -0.02em;
  color: var(--text);
}
.brand-badge {
  font-size: 10px;
  font-weight: 700;
  padding: 1px 5px;
  background: var(--brand-subtle);
  color: var(--brand);
  border-radius: 4px;
  letter-spacing: 0.05em;
}

.nav { display: flex; gap: 6px; flex: 1; margin-left: 12px; }
.nav-item {
  color: var(--muted);
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 13.5px;
  font-weight: 500;
  transition: all 0.15s ease;
  position: relative;
}
.nav-item:hover {
  color: var(--text);
  background: var(--card-subtle);
}
.nav-item.router-link-active {
  color: var(--brand);
  font-weight: 600;
  background: var(--brand-subtle);
}

.logout-btn {
  padding: 6px 12px;
  font-size: 12.5px;
}

.content {
  padding: 24px;
  max-width: 1200px;
  margin: 0 auto;
  width: 100%;
  box-sizing: border-box;
}

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
  background: rgba(15, 23, 42, 0.4);
  backdrop-filter: blur(4px);
  -webkit-backdrop-filter: blur(4px);
  z-index: 90;
}

.mobile-drawer {
  display: none;
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  width: 270px;
  max-width: 82vw;
  background: var(--card);
  z-index: 100;
  flex-direction: column;
  border-right: 1px solid var(--border);
  box-shadow: 4px 0 24px rgba(0, 0, 0, 0.12);
  transform: translateX(-100%);
  transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1);
}
.mobile-drawer.open { transform: translateX(0); }

.drawer-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 18px;
  border-bottom: 1px solid var(--border);
}
.drawer-brand { display: flex; align-items: center; gap: 8px; }
.drawer-title { font-weight: 700; font-size: 15px; color: var(--text); }
.close-btn {
  background: none;
  border: none;
  font-size: 16px;
  color: var(--muted);
  cursor: pointer;
  padding: 6px;
}
.drawer-nav {
  display: flex;
  flex-direction: column;
  padding: 12px 10px;
  gap: 4px;
  flex: 1;
}
.drawer-nav a {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 11px 14px;
  color: var(--text-secondary);
  border-radius: 8px;
  font-size: 14px;
  font-weight: 500;
}
.d-icon { font-size: 15px; }
.drawer-nav a.router-link-active {
  background: var(--brand-subtle);
  color: var(--brand);
  font-weight: 600;
}
.drawer-foot {
  padding: 16px;
  border-top: 1px solid var(--border);
}
.full-width { width: 100%; }

@media (max-width: 768px) {
  .topbar { padding: 0 16px; height: 52px; }
  .desktop-nav, .desktop-right { display: none; }
  .mobile-toggle { display: flex; }
  .drawer-overlay, .mobile-drawer { display: flex; }
  .content { padding: 16px 14px 28px; }
}

@media (prefers-color-scheme: dark) {
  .topbar {
    background: rgba(19, 27, 46, 0.85);
  }
}
</style>