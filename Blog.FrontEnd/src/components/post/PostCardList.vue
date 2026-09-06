<script setup lang="ts">
import type { PagedResult, PostListItemDto } from '@/types'
import PostCard from './PostCard.vue'
import PaginationBar from '@/components/common/PaginationBar.vue'

defineProps<{
  result: PagedResult<PostListItemDto> | null
  loading: boolean
  skeletonCount?: number
}>()

const emit = defineEmits<{ (e: 'page-change', page: number): void }>()
</script>

<template>
  <div class="post-list">
    <!-- 骨架屏 -->
    <div v-if="loading" class="grid">
      <div v-for="i in skeletonCount ?? 12" :key="i" class="skeleton-card card">
        <div class="sk-cover shimmer" />
        <div class="sk-body">
          <div class="sk-line shimmer w-70" />
          <div class="sk-line shimmer" />
          <div class="sk-line shimmer w-40" />
        </div>
      </div>
    </div>

    <!-- 空态 -->
    <div v-else-if="!result || result.items.length === 0" class="empty-state">
      <p class="empty-icon">📝</p>
      <p class="empty-text">暂无文章</p>
    </div>

    <!-- 卡片栅格：≥1200px 三列 / 768-1199 两列 / <768 单列 -->
    <template v-else>
      <div class="grid">
        <PostCard v-for="post in result.items" :key="post.id" :post="post" />
      </div>
      <PaginationBar
        :page="result.page"
        :total-pages="result.totalPages"
        :total-count="result.totalCount"
        @change="emit('page-change', $event)"
      />
    </template>
  </div>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-6);
}

@media (max-width: 1199px) {
  .grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 767px) {
  .grid {
    grid-template-columns: 1fr;
  }
}

/* 骨架屏 */
.skeleton-card {
  overflow: hidden;
}

.sk-cover {
  aspect-ratio: 16 / 9;
}

.sk-body {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-4);
}

.sk-line {
  height: 14px;
  border-radius: var(--radius-xs);
}

.w-70 {
  width: 70%;
}

.w-40 {
  width: 40%;
}

.shimmer {
  background: linear-gradient(90deg, var(--bg-raised) 25%, var(--border-subtle) 50%, var(--bg-raised) 75%);
  background-size: 200% 100%;
  animation: shimmer 1.4s infinite;
}

@keyframes shimmer {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}

/* 空态 */
.empty-state {
  padding: var(--space-24) 0;
  text-align: center;
}

.empty-icon {
  font-size: 42px;
  margin-bottom: var(--space-4);
}

.empty-text {
  color: var(--text-subtle);
}
</style>
