<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useSiteStore } from '@/stores/site'
import { useAuthStore } from '@/stores/auth'
import { logout as logoutApi } from '@/api/auth'
import SiteLogo from '@/components/common/SiteLogo.vue'

const route = useRoute()
const router = useRouter()
const site = useSiteStore()
const auth = useAuthStore()

async function onLogout() {
  try {
    // 后端提升 TokenVersion，使该账号所有旧 token 立即失效
    await logoutApi()
  } catch {
    /* 请求失败也要清理本地，避免登不出去 */
  }
  auth.clear()
  await router.replace({ name: 'login' })
}

interface AdminNavItem {
  to: string
  label: string
  icon: string
  /** 二级页面（如 /admin/posts/new）需要前缀匹配才能保持高亮 */
  prefix?: boolean
}

const navItems: AdminNavItem[] = [
  { to: '/admin', label: '文章管理', icon: 'M4 5h16M4 12h16M4 19h10', prefix: true },
  { to: '/admin/categories', label: '分类管理', icon: 'M4 6h6v6H4zM14 6h6v6h-6zM4 16h6v4H4zM14 16h6v4h-6z' },
  { to: '/admin/authors', label: '作者管理', icon: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm13 10v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75' },
  { to: '/admin/collections', label: '专栏管理', icon: 'M4 19.5A2.5 2.5 0 0 1 6.5 17H20M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2Z' },
  { to: '/admin/tags', label: '标签管理', icon: 'M20.6 13.4 12 22l-9-9V4h9l8.6 8.6a1 1 0 0 1 0 1.4ZM7.5 7.5h.01' },
  { to: '/admin/users', label: '账号管理', icon: 'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm14 10v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75' },
  { to: '/admin/site', label: '网站配置', icon: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-2.9 1.2v.2a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.6 1.7 1.7 0 0 0-1.9.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0-1.2-2.9H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.7 9a1.7 1.7 0 0 0-.4-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z' },
]

const isActive = (item: AdminNavItem) =>
  item.prefix ? route.path === item.to || route.path.startsWith(`${item.to}/`) : route.path === item.to

// 用「最长匹配」决定高亮与标题：/admin 是前缀项，否则访问 /admin/profile 会被 /admin 抢先命中
const currentItem = computed(() => {
  const matched = navItems.filter((item) => isActive(item))
  return matched.sort((a, b) => b.to.length - a.to.length)[0] ?? null
})

const currentTitle = computed(() => currentItem.value?.label ?? '管理后台')
</script>

<template>
  <div class="admin-shell">
    <aside class="admin-side">
      <div class="admin-brand">
        <SiteLogo :size="34" />
        <span class="brand-text">
          <strong>{{ site.config?.siteName ?? "kky's blog" }}</strong>
          <em>管理后台</em>
        </span>
      </div>

      <nav class="admin-nav">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="nav-item"
          :class="{ active: currentItem?.to === item.to }"
        >
          <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path :d="item.icon" />
          </svg>
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>

      <div class="side-footer">
        <span class="side-footer-label">快捷入口</span>
        <button class="back-link" @click="router.push('/me')">
          <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 20h9M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
          </svg>
          我的写作台
        </button>
        <button class="back-link" @click="router.push('/')">
          <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M19 12H5m0 0 6-6m-6 6 6 6" />
          </svg>
          返回前台
        </button>
      </div>
    </aside>

    <div class="admin-main">
      <header class="admin-topbar">
        <h1>{{ currentTitle }}</h1>
        <div class="topbar-right">
          <span class="who">
            <span class="who-email">{{ auth.user?.email }}</span>
            <span class="who-role">{{ auth.role }}</span>
          </span>
          <button class="btn-logout" @click="onLogout">退出登录</button>
        </div>
      </header>

      <div class="admin-content">
        <RouterView v-slot="{ Component }">
          <Transition name="fade" mode="out-in">
            <component :is="Component" />
          </Transition>
        </RouterView>
      </div>
    </div>
  </div>
</template>

<style scoped>
.admin-shell {
  flex: 1;
  display: flex;
  align-items: stretch;
  min-height: 100vh;
  background: var(--bg-canvas);
}

/* ---------- 侧边栏 ---------- */
.admin-side {
  width: 216px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
  padding: var(--space-6) var(--space-3);
  background: var(--bg-surface);
  border-right: 1px solid var(--border-subtle);
  /* 侧栏固定，不随右侧内容滚动。
     改之前这里没有任何高度约束，外层 .admin-shell 是 align-items: stretch，
     于是侧栏跟着内容一起变高、整个滚走 —— 长列表页上按钮就"消失"了。
     用 sticky + 100vh：内容比视口高时侧栏保持贴顶，自身超高时独立滚动。 */
  position: sticky;
  top: 0;
  height: 100vh;
  overflow-y: auto;
}

.admin-brand {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: 0 var(--space-3);
}

.brand-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.brand-text strong {
  font-size: 14px;
  font-weight: 700;
  color: var(--text-strong);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.brand-text em {
  font: var(--text-caption);
  font-style: normal;
  color: var(--text-subtle);
}

.admin-nav {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  flex: 1;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-3);
  font-size: 14px;
  font-weight: 500;
  color: var(--text-muted);
  border-radius: var(--radius-sm);
  transition: color var(--transition-fast), background var(--transition-fast);
}
.nav-item:hover {
  color: var(--text-strong);
  background: var(--bg-raised);
}
.nav-item.active {
  color: var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 12%, transparent);
}

/* 底部「快捷入口」。
   以前这里是两个透明的 13px 小字按钮（.back-link，颜色 text-subtle），
   与上方 .nav-item（有 padding/圆角/悬停底色/图标）视觉权重差太远，
   用户第一时间找不到 —— 现在改成与 .nav-item 完全同规格的样式。 */
.side-footer {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  padding-top: var(--space-3);
  border-top: 1px solid var(--border-subtle);
}

.side-footer-label {
  padding: 0 var(--space-3) var(--space-1);
  font: var(--text-caption);
  color: var(--text-subtle);
  text-transform: uppercase;
  letter-spacing: 1px;
}

.back-link {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  width: 100%;
  padding: var(--space-3);
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  font-size: 14px;
  font-weight: 500;
  color: var(--text-muted);
  text-align: left;
  cursor: pointer;
  transition: color var(--transition-fast), background var(--transition-fast);
}
.back-link:hover {
  color: var(--text-strong);
  background: var(--bg-raised);
}
.back-link svg {
  flex-shrink: 0;
}

/* ---------- 主内容区 ---------- */
.admin-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.admin-topbar {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
  padding: var(--space-6) var(--space-8) var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}
.admin-topbar h1 {
  font: var(--text-h2);
  color: var(--text-strong);
  margin: 0;
}
.topbar-right {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.who {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  line-height: 1.3;
}
.who-email {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-strong);
}
.who-role {
  font: var(--text-caption);
  color: var(--text-subtle);
}
.btn-logout {
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  font-size: 13px;
  color: var(--text-default);
  background: transparent;
  cursor: pointer;
}
.btn-logout:hover {
  border-color: #e35151;
  color: #e35151;
}

.admin-content {
  padding: var(--space-6) var(--space-8) var(--space-16);
  width: 100%;
  max-width: 1120px;
}

/* ---------- 窄屏：侧边栏折叠为横向滚动 tab ---------- */
@media (max-width: 880px) {
  .admin-shell {
    flex-direction: column;
  }
  .admin-side {
    width: 100%;
    flex-direction: row;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-3) var(--space-4);
    border-right: none;
    border-bottom: 1px solid var(--border-subtle);
    overflow-x: auto;
  }
  .admin-brand {
    padding: 0;
    flex-shrink: 0;
  }
  .brand-text em {
    display: none;
  }
  .admin-nav {
    flex-direction: row;
    flex-wrap: nowrap;
    gap: var(--space-1);
    flex: 0 0 auto;
  }
  .nav-item {
    padding: var(--space-2) var(--space-3);
    white-space: nowrap;
    flex-shrink: 0;
  }
  .nav-item span {
    font-size: 13px;
  }
  .back-link {
    flex-shrink: 0;
    border-top: none;
    border-left: 1px solid var(--border-subtle);
    padding: var(--space-2) var(--space-3);
    white-space: nowrap;
  }
  .admin-topbar {
    padding: var(--space-5) var(--space-4) var(--space-3);
  }
  .who {
    display: none;
  }
  .admin-content {
    padding: var(--space-4) var(--space-4) var(--space-16);
  }
}
</style>
