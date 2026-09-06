import { defineStore } from 'pinia'

const THEME_KEY = 'blog-theme'

/** 主题：暗色默认，localStorage 持久化，写入 <html data-theme> */
export const useThemeStore = defineStore('theme', {
  state: () => ({
    theme: (localStorage.getItem(THEME_KEY) as 'dark' | 'light') || 'dark',
  }),
  actions: {
    apply() {
      document.documentElement.dataset.theme = this.theme
    },
    toggle() {
      this.theme = this.theme === 'dark' ? 'light' : 'dark'
      localStorage.setItem(THEME_KEY, this.theme)
      this.apply()
    },
  },
})

/** 搜索弹窗开关 */
export const useSearchStore = defineStore('search', {
  state: () => ({ open: false }),
  actions: {
    show() {
      this.open = true
    },
    hide() {
      this.open = false
    },
  },
})
