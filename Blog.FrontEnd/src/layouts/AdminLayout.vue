<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useSiteStore } from '@/stores/site'

const route = useRoute()
const router = useRouter()
const site = useSiteStore()

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
  { to: '/admin/tags', label: '标签管理', icon: 'M20.6 13.4 12 22l-9-9V4h9l8.6 8.6a1 1 0 0 1 0 1.4ZM7.5 7.5h.01' },
  { to: '/admin/profile', label: '用户资料', icon: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z' },
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
        <span class="logo-dot">k</span>
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

      <button class="back-link" @click="router.push('/')">
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M19 12H5m0 0 6-6m-6 6 6 6" />
        </svg>
        返回前台
      </button>
    </aside>

    <div class="admin-main">
      <header class="admin-topbar">
        <h1>{{ currentTitle }}</h1>
        <span class="topbar-hint">当前无认证，写操作按匿名放行（待后续补充）</span>
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
}

.admin-brand {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: 0 var(--space-3);
}

.logo-dot {
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  flex-shrink: 0;
  border-radius: 50%;
  font-size: 16px;
  font-weight: 700;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  box-shadow: var(--glow-purple);
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

.back-link {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-3);
  font-size: 13px;
  color: var(--text-subtle);
  border-top: 1px solid var(--border-subtle);
  transition: color var(--transition-fast);
}
.back-link:hover {
  color: var(--brand-500);
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
.topbar-hint {
  font: var(--text-caption);
  color: var(--text-subtle);
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
  .topbar-hint {
    display: none;
  }
  .admin-content {
    padding: var(--space-4) var(--space-4) var(--space-16);
  }
}
</style>
