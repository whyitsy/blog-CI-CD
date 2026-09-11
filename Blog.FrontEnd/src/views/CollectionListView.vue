<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { getCollections } from '@/api/collections'
import TaxonomySkeleton from '@/components/skeleton/TaxonomySkeleton.vue'
import type { CollectionDto } from '@/types'

const collections = ref<CollectionDto[]>([])
const loading = ref(true)

async function load() {
  loading.value = true
  try {
    collections.value = await getCollections()
  } catch (e) {
    // 加载失败按「空列表」呈现，与分类墙/标签墙等公开页保持一致：
    // 技术细节只进控制台，页面不暴露 HTTP 状态码等内部信息，也不弹错误横幅。
    // 注意：公开页的失败提示一旦做成横幅，就会把「服务未启动」演变成
    // 「暂无专栏」不显示 —— 与本项目其他公开列表页的行为不一致。
    collections.value = []
    console.error('[collections] 加载专栏列表失败：', e)
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="collections-view">
    <header class="page-header">
      <div class="aurora-blobs" />
      <p class="eyebrow">COLLECTIONS · 共 {{ collections.length }} 个专栏</p>
      <h1 class="page-title gradient-text">专栏</h1>
      <p class="page-sub">把多篇文章组织成一个系列，按顺序阅读。</p>
    </header>

    <div class="container list-container">
      <TaxonomySkeleton v-if="loading" :count="6" />

      <!-- 与分类墙/标签墙一致：无数据显示「暂无」，加载失败也走这里（错误只进控制台） -->
      <p v-else-if="collections.length === 0" class="empty">暂无专栏</p>

      <div v-else class="grid">
        <RouterLink
          v-for="c in collections"
          :key="c.id"
          :to="`/collections/${c.slug}`"
          class="collection-card card"
        >
          <div class="cover">
            <img v-if="c.coverImage" :src="c.coverImage" :alt="c.title" loading="lazy" />
            <div v-else class="cover-placeholder" />
          </div>
          <div class="body">
            <h2 class="title">{{ c.title }}</h2>
            <p class="desc">{{ c.description || '暂无简介' }}</p>
            <span class="count">{{ c.postCount }} 篇文章</span>
          </div>
        </RouterLink>
      </div>
    </div>
  </div>
</template>

<style scoped>
.collections-view {
  padding-top: 64px;
  min-height: 70vh;
}

.page-header {
  position: relative;
  padding: var(--space-16) var(--space-6) var(--space-12);
  text-align: center;
  overflow: hidden;
}

.eyebrow {
  position: relative;
  font: var(--text-caption);
  letter-spacing: 3px;
  color: var(--text-muted);
  margin-bottom: var(--space-3);
}

.page-title {
  position: relative;
  font: var(--text-h1);
  margin-bottom: var(--space-3);
}

.page-sub {
  position: relative;
  font: var(--text-body-sm);
  color: var(--text-subtle);
}

.list-container {
  padding-bottom: var(--space-16);
}

.empty {
  padding: var(--space-16) 0;
  text-align: center;
  color: var(--text-subtle);
}

.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-6);
}

.collection-card {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.collection-card:hover {
  transform: translateY(-4px);
  border-color: var(--border-default);
  box-shadow: var(--shadow-md), var(--glow-purple);
}

.cover {
  aspect-ratio: 16 / 9;
  overflow: hidden;
}
.cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.cover-placeholder {
  width: 100%;
  height: 100%;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  opacity: 0.85;
}

.body {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-4);
}

.title {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}

.desc {
  font: var(--text-body-sm);
  color: var(--text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.count {
  font: var(--text-caption);
  color: var(--brand-500);
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
</style>
