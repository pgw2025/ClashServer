import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// 开发期代理 /api 到后端，保持浏览器视角同源；同名 Cookie 正常生效、无 CORS
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true
      }
    }
  },
  // base:'./' → index.html 用相对路径引用 assets，部署到子路径也不会资源 404
  base: './',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true
  }
})