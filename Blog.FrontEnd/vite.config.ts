import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // 读取 .env / .env.[mode] / .env.[mode].local（第三个参数 '' 表示加载全部变量，不只是 VITE_ 前缀）
  const env = loadEnv(mode, process.cwd(), '')

  // 代理目标来自环境变量，避免把后端端口硬编码在配置里（见 docs/04-前端设计.md §1.3）
  const proxyTarget = env.VITE_PROXY_TARGET

  return {
    plugins: [vue()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      proxy: proxyTarget
        ? {
            // 后端接口与媒体文件统一走代理，避免跨域；coverImage 等相对路径可直接使用
            '/api': {
              target: proxyTarget,
              changeOrigin: true,
            },
          }
        : undefined,
    },
  }
})
