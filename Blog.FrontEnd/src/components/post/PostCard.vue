<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import type { PostListItemDto } from '@/types'

const props = defineProps<{ post: PostListItemDto }>()

const publishDate = computed(() => {
  if (!props.post.publishedAt) return '未发布'
  return new Date(props.post.publishedAt).toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  })
})
</script>

<template>
  <RouterLink :to="`/post/${post.id}`" class="post-card card">
    <div class="cover">
      <img v-if="post.coverImage" :src="post.coverImage" :alt="post.title" loading="lazy" />
      <div v-else class="cover-placeholder" />
      <!-- 分类：可点击，跳到该分类的筛选列表（用全局 .category-badge 与标签徽章区分） -->
      <RouterLink
        v-if="post.categoryId"
        :to="{ path: '/posts', query: { categoryId: post.categoryId } }"
        class="cover-category category-badge"
        @click.stop
      >
        {{ post.categoryName }}
      </RouterLink>
    </div>

    <div class="card-body">
      <h3 class="card-title">{{ post.title }}</h3>
      <p class="card-summary">{{ post.summary }}</p>

      <div class="card-meta">
        <time class="meta-date">{{ publishDate }}</time>
        <span class="meta-views">
          <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z" />
            <circle cx="12" cy="12" r="3" />
          </svg>
          {{ post.viewCount }}
        </span>
      </div>

      <div v-if="post.tags.length" class="card-tags">
        <span v-for="tag in post.tags" :key="tag.id" class="tag-badge">{{ tag.name }}</span>
      </div>
    </div>
  </RouterLink>
</template>

<style scoped>
.post-card {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.post-card:hover {
  transform: translateY(-4px);
  border-color: var(--border-default);
  box-shadow: var(--shadow-md), var(--glow-purple);
}

.cover {
  position: relative;
  aspect-ratio: 16 / 9;
  overflow: hidden;
  background: var(--bg-sunken);
}

.cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform var(--transition-normal);
}

.post-card:hover .cover img {
  transform: scale(1.04);
}

.cover-placeholder {
  width: 100%;
  height: 100%;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  opacity: 0.85;
}

/* 只负责「钉在封面左上角」的定位；
   视觉（渐变底色/圆角/字号）全部交给全局 .category-badge。
   注意：不要在这里再写 background/color，否则会覆盖全局样式
   （scoped 选择器带 data-v 属性，优先级高于全局类）。 */
.cover-category {
  position: absolute;
  top: var(--space-3);
  left: var(--space-3);
  z-index: 2;
}

.card-body {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-4);
  flex: 1;
}

.card-title {
  font-size: 17px;
  font-weight: 600;
  line-height: 1.4;
  color: var(--text-strong);
  display: -webkit-box;
  -webkit-line-clamp: 1;
  -webkit-box-orient: vertical;
  overflow: hidden;
  transition: color var(--transition-fast);
}

.post-card:hover .card-title {
  color: var(--brand-500);
}

.card-summary {
  font: var(--text-body-sm);
  color: var(--text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  flex: 1;
}

.card-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font: var(--text-caption);
  color: var(--text-subtle);
}

.meta-views {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
</style>
