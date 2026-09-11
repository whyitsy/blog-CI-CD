<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { getCollectionBySlug } from '@/api/collections'
import { isNotFoundError } from '@/api/http'
import { PostDetailSkeleton } from '@/components/skeleton'
import type { CollectionDetailDto } from '@/types'

const route = useRoute()

const detail = ref<CollectionDetailDto | null>(null)
const loading = ref(true)
const notFound = ref(false)
const loadFailed = ref(false)

const slug = computed(() => String(route.params.slug ?? ''))

async function load(s: string) {
  loading.value = true
  notFound.value = false
  loadFailed.value = false
  detail.value = null
  try {
    detail.value = await getCollectionBySlug(s)
  } catch (e) {
    // 后端对「不存在」与「未发布且非管理员」都返回 404，避免探测。
    // 其余错误（网络异常、502/503、500…）是服务端故障，不能当成「专栏不存在」，
    // 也不把内部错误信息展示到页面上——技术细节只写控制台，页面给中性提示。
    if (isNotFoundError(e)) {
      notFound.value = true
    } else {
      loadFailed.value = true
      console.error('[collection-detail] 加载专栏失败：', e)
    }
  } finally {
    loading.value = false
  }
}

watch(slug, (s) => s && load(s), { immediate: true })

function fmtDate(iso: string | null) {
  return iso ? iso.slice(0, 10) : ''
}
</script>

<template>
  <div class="collection-detail">
    <PostDetailSkeleton v-if="loading" />

    <template v-else-if="notFound">
      <div class="container not-found">
        <p class="nf-icon">📚</p>
        <h1 class="nf-title">专栏不存在</h1>
        <p class="nf-desc">该专栏可能已被删除，或尚未发布。</p>
        <div class="nf-actions">
          <RouterLink to="/collections" class="nf-link">查看全部专栏</RouterLink>
        </div>
      </div>
    </template>

    <template v-else-if="loadFailed">
      <div class="container not-found">
        <p class="nf-icon">⚠️</p>
        <h1 class="nf-title">加载失败</h1>
        <p class="nf-desc">服务暂时不可用，请稍后重试。</p>
        <div class="nf-actions">
          <button type="button" class="nf-link nf-btn" @click="load(slug)">重新加载</button>
          <RouterLink to="/collections" class="nf-link">查看全部专栏</RouterLink>
        </div>
      </div>
    </template>

    <template v-else-if="detail">
      <header class="page-header">
        <div class="aurora-blobs" />
        <p class="eyebrow">COLLECTION</p>
        <h1 class="page-title gradient-text">{{ detail.title }}</h1>
        <p v-if="detail.description" class="page-sub">{{ detail.description }}</p>
        <p class="meta">共 {{ detail.posts.length }} 篇文章 · 按专栏顺序阅读</p>
      </header>

      <div class="container body">
        <ol v-if="detail.posts.length" class="post-list">
          <li v-for="(p, index) in detail.posts" :key="p.id" class="post-item card">
            <span class="order">{{ index + 1 }}</span>
            <div class="post-main">
              <RouterLink :to="`/post/${p.id}`" class="post-title">{{ p.title }}</RouterLink>
              <p class="post-summary">{{ p.summary }}</p>
              <div class="post-meta">
                <span>{{ fmtDate(p.publishedAt) }}</span>
                <span>·</span>
                <span>{{ p.viewCount }} 次浏览</span>
              </div>
            </div>
            <RouterLink :to="`/post/${p.id}`" class="read-link">阅读 →</RouterLink>
          </li>
        </ol>

        <p v-else class="empty">该专栏还没有文章</p>

        <RouterLink to="/collections" class="back-link">← 返回专栏列表</RouterLink>
      </div>
    </template>
  </div>
</template>

<style scoped>
.collection-detail {
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
  font: var(--text-body);
  color: var(--text-muted);
  max-width: 640px;
  margin: 0 auto var(--space-3);
}

.meta {
  position: relative;
  font: var(--text-caption);
  color: var(--text-subtle);
}

.body {
  padding-bottom: var(--space-16);
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}

.post-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  list-style: none;
  padding: 0;
  margin: 0;
}

.post-item {
  display: grid;
  grid-template-columns: 44px minmax(0, 1fr) auto;
  gap: var(--space-4);
  align-items: center;
  padding: var(--space-5);
}

.order {
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  font-weight: 700;
  color: var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 12%, transparent);
}

.post-main {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  min-width: 0;
}

.post-title {
  font-size: 17px;
  font-weight: 600;
  color: var(--text-strong);
}
.post-title:hover {
  color: var(--brand-500);
}

.post-summary {
  font: var(--text-body-sm);
  color: var(--text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.post-meta {
  display: flex;
  gap: var(--space-2);
  font: var(--text-caption);
  color: var(--text-subtle);
}

.read-link {
  font-size: 14px;
  color: var(--brand-500);
  white-space: nowrap;
}

.empty {
  padding: var(--space-16) 0;
  text-align: center;
  color: var(--text-subtle);
}

.back-link {
  font: var(--text-body-sm);
  color: var(--text-muted);
  align-self: flex-start;
}
.back-link:hover {
  color: var(--brand-500);
}

.not-found {
  padding: var(--space-24) 0;
  text-align: center;
}
.nf-icon {
  font-size: 42px;
  margin-bottom: var(--space-3);
}
.nf-title {
  font: var(--text-h2);
  color: var(--text-strong);
  margin-bottom: var(--space-3);
}
.nf-desc {
  font: var(--text-body-sm);
  color: var(--text-muted);
  margin-bottom: var(--space-5);
}
.nf-actions {
  display: flex;
  gap: var(--space-5);
  justify-content: center;
  align-items: center;
}
.nf-link {
  color: var(--brand-500);
  font-size: 14px;
}
.nf-btn {
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  font-family: inherit;
}
.nf-btn:hover,
.nf-link:hover {
  text-decoration: underline;
}

@media (max-width: 767px) {
  .post-item {
    grid-template-columns: 32px minmax(0, 1fr);
  }
  .read-link {
    display: none;
  }
}
</style>
