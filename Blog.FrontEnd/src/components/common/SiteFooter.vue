<script setup lang="ts">
import { computed } from 'vue'
import { useSiteStore } from '@/stores/site'

const site = useSiteStore()

const year = new Date().getFullYear()

const statsItems = computed(() => {
  const s = site.stats
  if (!s) return []
  return [
    { label: '建站天数', value: `${s.siteDays} 天` },
    { label: '文章', value: `${s.totalPosts} 篇` },
    { label: '总字数', value: formatWords(s.totalWords) },
    { label: '总浏览', value: formatViews(s.totalViews) },
  ]
})

function formatWords(n: number) {
  return n >= 10000 ? `${(n / 10000).toFixed(1)}w` : `${n}`
}
function formatViews(n: number) {
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${n}`
}
</script>

<template>
  <footer class="site-footer">
    <div class="footer-gradient-line" />
    <div class="container footer-inner">
      <div class="footer-brand">
        <div class="footer-logo">
          <span class="logo-dot">k</span>
          <span class="site-name">{{ site.config?.siteName ?? "kky's blog" }}</span>
        </div>
        <p class="copyright">© {{ year }} kky · 记录代码与生活</p>
      </div>

      <div v-if="statsItems.length" class="footer-stats">
        <div v-for="item in statsItems" :key="item.label" class="stat-item">
          <span class="stat-value gradient-text">{{ item.value }}</span>
          <span class="stat-label">{{ item.label }}</span>
        </div>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.site-footer {
  margin-top: auto;
  background: var(--bg-surface);
  border-top: 1px solid var(--border-subtle);
}

.footer-gradient-line {
  height: 2px;
  background: linear-gradient(90deg, transparent, var(--gradient-start), var(--gradient-mid), var(--gradient-end), transparent);
  opacity: 0.7;
}

.footer-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-6);
  padding-top: var(--space-8);
  padding-bottom: var(--space-8);
  flex-wrap: wrap;
}

.footer-logo {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  margin-bottom: var(--space-2);
}

.logo-dot {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  font-size: 14px;
  font-weight: 700;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
}

.site-name {
  font-weight: 700;
  color: var(--text-strong);
}

.copyright {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.footer-stats {
  display: flex;
  gap: var(--space-8);
  flex-wrap: wrap;
}

.stat-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

.stat-value {
  font-size: 20px;
  font-weight: 700;
}

.stat-label {
  font: var(--text-caption);
  color: var(--text-subtle);
}
</style>
