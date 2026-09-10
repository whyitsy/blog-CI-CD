import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { useThemeStore, useSearchStore } from './stores/app'
import { useAuthStore } from './stores/auth'
import { configureHttp, ErrorCode, ApiError } from './api/http'
import { typewriter } from './directives/typewriter'
import './styles/tokens.css'
import './styles/global.css'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)
app.directive('typewriter', typewriter)

// 应用持久化主题（dark 默认）
useThemeStore().apply()

// ---------------------------------------------------------------- 认证接线
const auth = useAuthStore(pinia)

// http 层不直接依赖 store/router（避免循环引用），由这里注入取值与 401 处理
configureHttp({
  getToken: () => auth.token,
  onUnauthorized: () => {
    // 已经在登录页时不要跳转，否则会把「密码错误」提示冲掉
    const onLoginPage = router.currentRoute.value.meta.guestOnly === true
    auth.clear()
    if (!onLoginPage) {
      const returnUrl = router.currentRoute.value.fullPath
      void router.replace({ name: 'login', query: returnUrl !== '/' ? { returnUrl } : {} })
    }
  },
})

// 搜索弹窗的全局快捷键（Ctrl/Cmd + K）
window.addEventListener('keydown', (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    useSearchStore(pinia).show()
  }
})

export { ApiError, ErrorCode }

// 启动时恢复会话（本地有 token 则向后端确认一次，失败即清理）
void auth.restore().finally(() => {
  app.mount('#app')
})
