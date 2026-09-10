import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
    {
      path: '/',
      component: () => import('@/components/AppLayout.vue'),
      children: [
        { path: '', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
        { path: 'rules', name: 'rules', component: () => import('@/views/RulesView.vue') },
        { path: 'rules/import', name: 'rules-import', component: () => import('@/views/RulesImportView.vue') },
        { path: 'config', name: 'config', component: () => import('@/views/ConfigView.vue') },
        { path: 'settings', name: 'settings', component: () => import('@/views/SettingsView.vue') }
      ]
    }
  ]
})

// 全局守卫：受保护路由未登录 → /login?redirect=目标路径，登录后回跳
router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (!auth.checked) await auth.check()
  if (to.meta.public) {
    // 已登录访问 login 则回首页
    if (to.path === '/login' && auth.loggedIn) return { path: '/' }
    return true
  }
  if (!auth.loggedIn) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }
  return true
})

export default router