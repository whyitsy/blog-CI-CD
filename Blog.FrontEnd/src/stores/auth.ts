import { defineStore } from 'pinia'
import { getMe } from '@/api/auth'
import type { AuthUser, LoginResponse, UserRole } from '@/types'

/**
 * token 与用户信息的持久化键。
 *
 * 存 localStorage 的权衡（见 learn/01-后端知识地图.md §8.4）：
 *   - 优点：不会自动随请求发送，因此**不引入 CSRF 问题**
 *   - 缺点：JS 可读，一旦 XSS 即被窃取 → 用「短有效期(30min)」+ CSP 补偿
 * token 过期后 `restore()` 会清掉本地状态。
 */
const TOKEN_KEY = 'blog-auth-token'
const USER_KEY = 'blog-auth-user'
const EXPIRES_KEY = 'blog-auth-expires'

function readUser(): AuthUser | null {
  const raw = localStorage.getItem(USER_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as AuthUser
  } catch {
    return null
  }
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: localStorage.getItem(TOKEN_KEY) ?? '',
    user: readUser(),
    expiresAt: localStorage.getItem(EXPIRES_KEY) ?? '',
    /** 是否已向后端确认过 token 有效性（避免每次导航都请求 /me） */
    verified: false,
  }),

  getters: {
    isAuthenticated: (s) => Boolean(s.token && s.user),

    role: (s): UserRole | null => s.user?.role ?? null,

    isAdmin(): boolean {
      return this.role === 'Admin'
    },

    /** 能写内容的人：管理员或作者（对应后端 ContentWriter 策略） */
    canWriteContent(): boolean {
      return this.role === 'Admin' || this.role === 'Author'
    },

    displayName: (s) => s.user?.authorName || s.user?.email || '',
  },

  actions: {
    /** 保存登录结果 */
    setSession(data: LoginResponse) {
      this.token = data.token
      this.user = data.user
      this.expiresAt = data.expiresAt
      this.verified = true

      localStorage.setItem(TOKEN_KEY, data.token)
      localStorage.setItem(USER_KEY, JSON.stringify(data.user))
      localStorage.setItem(EXPIRES_KEY, data.expiresAt)
    },

    /** 清空本地登录状态（不请求后端） */
    clear() {
      this.token = ''
      this.user = null
      this.expiresAt = ''
      this.verified = false

      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
      localStorage.removeItem(EXPIRES_KEY)
    },

    /**
     * 启动/刷新时恢复会话：本地有 token 就向后端确认一次。
     * 这样能正确处理「token 未过期但账号已被停用/改密」的情况
     * （后端 TokenVersion 校验会拒绝，/me 返回 401）。
     */
    async restore() {
      if (!this.token) {
        this.clear()
        return
      }

      // 本地已知过期 -> 直接清理，省一次请求
      if (this.expiresAt && new Date(this.expiresAt).getTime() <= Date.now()) {
        this.clear()
        return
      }

      try {
        const me = await getMe()
        this.user = me
        localStorage.setItem(USER_KEY, JSON.stringify(me))
        this.verified = true
      } catch {
        // 401（未认证/已过期/被踢下线）或其他失败：清空本地状态
        this.clear()
      }
    },
  },
})
