<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import TaxonomySkeleton from '@/components/skeleton/TaxonomySkeleton.vue'
import { getTags } from '@/api/tags'
import type { TagDto } from '@/types'

const router = useRouter()
const tags = ref<TagDto[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    tags.value = await getTags()
  } finally {
    loading.value = false
  }
})

function goTag(id: string) {
  router.push({ path: '/posts', query: { tagId: id } })
}
</script>

<template>
  <div class="tags-view">
    <header class="page-header">
      <p class="eyebrow">TAGS · 共 {{ tags.length }} 个标签</p>
      <h1 class="page-title gradient-text">标签墙</h1>
      <p class="page-sub">点击标签，查看该标签下的全部文章。</p>
    </header>

    <div class="container wall-container">
      <TaxonomySkeleton v-if="loading" :count="10" />
      <p v-else-if="tags.length === 0" class="empty">暂无标签</p>
      <div v-else class="wall">
        <button
          v-for="tag in tags"
          :key="tag.id"
          class="wall-btn"
          @click="goTag(tag.id)"
        >
          <span class="wall-name">{{ tag.name }}</span>
          <span class="wall-count">{{ tag.postCount }}</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tags-view {
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
  font: var(--text-caption);
  letter-spacing: 3px;
  color: var(--text-subtle);
  margin-bottom: var(--space-3);
}

.page-title {
  font: var(--text-h1);
}

.page-sub {
  margin-top: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-muted);
}

.wall-container {
  padding-bottom: var(--space-16);
}

.wall {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: var(--space-4);
  max-width: 860px;
  margin: 0 auto;
}

.wall-btn {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-5);
  font-size: 15px;
  font-weight: 500;
  color: var(--text-default);
  background: var(--bg-surface);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-2xl);
  transition: all var(--transition-fast);
}

.wall-btn:hover {
  color: #fff;
  border-color: transparent;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  transform: translateY(-2px);
  box-shadow: var(--glow-purple);
}

.wall-count {
  min-width: 22px;
  padding: 1px var(--space-2);
  font: var(--text-caption);
  text-align: center;
  color: var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 12%, transparent);
  border-radius: var(--radius-2xl);
}

.wall-btn:hover .wall-count {
  color: #fff;
  background: rgba(255, 255, 255, 0.22);
}

.empty {
  padding: var(--space-16) 0;
  text-align: center;
  color: var(--text-subtle);
}
</style>
