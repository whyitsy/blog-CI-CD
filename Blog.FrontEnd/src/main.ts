import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { useThemeStore } from './stores/app'
import { typewriter } from './directives/typewriter'
import './styles/tokens.css'
import './styles/global.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.directive('typewriter', typewriter)

// 应用持久化主题（dark 默认）
useThemeStore().apply()

app.mount('#app')
