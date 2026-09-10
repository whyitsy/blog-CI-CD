import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      // 后端接口与媒体文件统一走代理，避免跨域；coverImage 等相对路径可直接使用
      '/api': {
        target: 'http://localhost:5131',
        changeOrigin: true,
      },
    },
  },
})
