<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import ArchiveSkeleton from '@/components/skeleton/ArchiveSkeleton.vue'
import { getArchives } from '@/api/posts'
import type { ArchiveGroupDto } from '@/types'

const groups = ref<ArchiveGroupDto[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    groups.value = await getArchives()
  } finally {
    loading.value = false
  }
})

const total = computed(() => groups.value.reduce((sum, g) => sum + g.items.length, 0))

/** 按年份再分组（年月 → 年） */
const byYear = computed(() => {
  const map = new Map<number, ArchiveGroupDto[]>()
  for (const g of groups.value) {
    const list = map.get(g.year) ?? []
    list.push(g)
    map.set(g.year, list)
  }
  return [...map.entries()].sort((a, b) => b[0] - a[0])
})

function dayOf(iso: string) {
  const d = new Date(iso)
  return `${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
</script>

<template>
  <div class="archive-view">
    <header class="page-header">
      <div class="aurora-blobs" />
      <p class="eyebrow">ARCHIVE · 共 {{ total }} 篇文章</p>
      <h1 class="page-title gradient-text">所有文章</h1>
      <p class="page-sub">按时间倒序排列，记录每一步的足迹。</p>
    </header>

    <div class="container timeline-container">
      <ArchiveSkeleton v-if="loading" :groups="3" />

      <p v-else-if="total === 0" class="empty">暂无文章</p>

      <div v-else class="timeline">
        <section v-for="[year, months] in byYear" :key="year" class="year-block">
          <div class="year-node">
            <span class="year-dot" />
            <h2 class="year-title">{{ year }}</h2>
          </div>

          <div v-for="group in months" :key="`${group.year}-${group.month}`" class="month-block">
            <!-- 年月标题统一左对齐（修正参考设计中的错位） -->
            <h3 class="month-title">
              <span class="month-num">{{ String(group.month).padStart(2, '0') }}</span>
              <span class="month-count">共 {{ group.items.length }} 篇</span>
            </h3>

            <ul class="post-rows">
              <li v-for="item in group.items" :key="item.id" class="post-row">
                <time class="row-date">{{ dayOf(item.publishedAt) }}</time>
                <RouterLink :to="`/post/${item.id}`" class="row-title">{{ item.title }}</RouterLink>
                <span class="row-dot" />
              </li>
            </ul>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>

<style scoped>
.archive-view {
  padding-top: 64px;
  min-height: 70vh;
}

.page-header {
  position: relative;
  padding: var(--space-16) var(--space-6) var(--space-10);
  text-align: center;
  overflow: hidden;
}

.eyebrow {
  position: relative;
  font: var(--text-caption);
  letter-spacing: 3px;
  color: var(--text-subtle);
  margin-bottom: var(--space-3);
}

.page-title {
  position: relative;
  font: var(--text-h1);
}

.page-sub {
  position: relative;
  margin-top: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-muted);
}

.timeline-container {
  max-width: 760px;
  padding-bottom: var(--space-16);
}

.timeline {
  position: relative;
  padding-left: var(--space-8);
}

/* 时间轴主线 */
.timeline::before {
  content: '';
  position: absolute;
  left: 7px;
  top: 12px;
  bottom: 12px;
  width: 2px;
  background: linear-gradient(180deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  opacity: 0.5;
  border-radius: 1px;
}

.year-block {
  margin-bottom: var(--space-10);
}

.year-node {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-3);
  margin-bottom: var(--space-5);
  margin-left: calc(-1 * var(--space-8));
}

.year-dot {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
  box-shadow: var(--glow-cyan);
  flex-shrink: 0;
}

.year-title {
  font-size: 30px;
  font-weight: 700;
  color: var(--text-strong);
}

.month-block {
  margin-bottom: var(--space-6);
}

.month-title {
  display: flex;
  align-items: baseline;
  gap: var(--space-3);
  margin-bottom: var(--space-3);
}

.month-num {
  font-size: 20px;
  font-weight: 700;
  color: var(--brand-500);
}

.month-count {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.post-rows {
  display: flex;
  flex-direction: column;
}

.post-row {
  position: relative;
  display: flex;
  align-items: baseline;
  gap: var(--space-4);
  padding: var(--space-2) 0;
}

/* 行节点小圆点 */
.row-dot {
  position: absolute;
  left: calc(-1 * var(--space-8) + 3px);
  top: 50%;
  transform: translateY(-50%);
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--bg-canvas);
  border: 2px solid var(--border-strong);
  transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
}

.post-row:hover .row-dot {
  border-color: var(--brand-500);
  box-shadow: var(--glow-cyan);
}

.row-date {
  flex-shrink: 0;
  width: 48px;
  font: var(--text-caption);
  color: var(--text-subtle);
}

.row-title {
  font-size: 15px;
  font-weight: 500;
  color: var(--text-default);
  transition: color var(--transition-fast);
}

.post-row:hover .row-title {
  color: var(--brand-500);
}

.empty {
  padding: var(--space-16) 0;
  text-align: center;
  color: var(--text-subtle);
}
</style>
