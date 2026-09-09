<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useSearchStore, useThemeStore } from '@/stores/app'
import { useSiteStore } from '@/stores/site'

const route = useRoute()
const theme = useThemeStore()
const search = useSearchStore()
const site = useSiteStore()

// 滚动超过阈值后导航栏加毛玻璃背景（首页 Hero 上保持透明）
const scrolled = ref(false)
const onScroll = () => {
  scrolled.value = window.scrollY > 40
}
onMounted(() => window.addEventListener('scroll', onScroll, { passive: true }))
onBeforeUnmount(() => window.removeEventListener('scroll', onScroll))

const navItems = [
  { name: 'home', label: '首页', to: '/' },
  { name: 'tags', label: '标签', to: '/tags' },
  { name: 'categories', label: '分类', to: '/categories' },
  { name: 'archive', label: '归档', to: '/archive' },
  { name: 'admin-posts', label: '管理', to: '/admin' },
]

const isActive = (name: string) => route.name === name
</script>

<template>
  <header class="navbar" :class="{ glass: scrolled }">
    <div class="navbar-inner container">
      <RouterLink to="/" class="logo">
        <span class="logo-dot">k</span>
        <span class="logo-name">{{ site.config?.siteName ?? "kky's blog" }}</span>
      </RouterLink>

      <nav class="nav-links">
        <RouterLink
          v-for="item in navItems"
          :key="item.name"
          :to="item.to"
          class="nav-link"
          :class="{ active: isActive(item.name) }"
        >
          {{ item.label }}
        </RouterLink>
      </nav>

      <div class="nav-actions">
        <button class="action-btn search-btn" aria-label="搜索" @click="search.show()">
          <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-3.5-3.5" />
          </svg>
          <span class="search-text">搜索文章…</span>
        </button>
        <button class="action-btn icon-btn" :aria-label="theme.theme === 'dark' ? '切换亮色' : '切换暗色'" @click="theme.toggle()">
          <svg v-if="theme.theme === 'dark'" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <circle cx="12" cy="12" r="4" />
            <path d="M12 2v2m0 16v2M4.9 4.9l1.4 1.4m11.4 11.4 1.4 1.4M2 12h2m16 0h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
          </svg>
          <svg v-else viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" />
          </svg>
        </button>
      </div>
    </div>
  </header>
</template>

<style scoped>
.navbar {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 100;
  transition: background var(--transition-normal), box-shadow var(--transition-normal);
}

.navbar-inner {
  display: flex;
  align-items: center;
  gap: var(--space-8);
  height: 64px;
}

.logo {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  font-weight: 700;
  font-size: 17px;
  color: var(--text-strong);
}

.logo-dot {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  font-size: 16px;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  box-shadow: var(--glow-purple);
}

.nav-links {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: 1;
}

.nav-link {
  position: relative;
  padding: var(--space-2) var(--space-3);
  font-size: 14px;
  font-weight: 500;
  color: var(--text-muted);
  border-radius: var(--radius-sm);
  transition: color var(--transition-fast);
}

.nav-link:hover {
  color: var(--text-strong);
}

.nav-link.active {
  color: var(--brand-500);
}

.nav-link.active::after {
  content: '';
  position: absolute;
  left: var(--space-3);
  right: var(--space-3);
  bottom: 2px;
  height: 2px;
  border-radius: 1px;
  background: linear-gradient(90deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
}

.nav-actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.action-btn {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  height: 36px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-2xl);
  border: 1px solid var(--border-default);
  color: var(--text-muted);
  font-size: 13px;
  transition: color var(--transition-fast), border-color var(--transition-fast), background var(--transition-fast);
}

.action-btn:hover {
  color: var(--text-strong);
  border-color: var(--border-strong);
  background: var(--bg-raised);
}

.icon-btn {
  width: 36px;
  padding: 0;
  justify-content: center;
}

.search-text {
  color: var(--text-subtle);
}

@media (max-width: 640px) {
  .search-text {
    display: none;
  }
  .navbar-inner {
    gap: var(--space-3);
  }
}
</style>
