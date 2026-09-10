<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PostCardList from '@/components/post/PostCardList.vue'
import { getPosts, searchPosts } from '@/api/posts'
import { getCategories } from '@/api/categories'
import { getTags } from '@/api/tags'
import type { PagedResult, PostListItemDto } from '@/types'

const route = useRoute()
const router = useRouter()

const result = ref<PagedResult<PostListItemDto> | null>(null)
const loading = ref(true)
const filterName = ref('')

const keyword = computed(() => String(route.query.keyword ?? ''))
const tagId = computed(() => String(route.query.tagId ?? ''))
const categoryId = computed(() => String(route.query.categoryId ?? ''))
const page = computed(() => Math.max(1, Number(route.query.page) || 1))

const headerTitle = computed(() => {
  if (keyword.value) return `搜索「${keyword.value}」`
  if (filterName.value) return filterName.value
  return '文章列表'
})

async function load() {
  loading.value = true
  try {
    if (keyword.value) {
      result.value = await searchPosts(keyword.value, page.value, 12)
    } else {
      result.value = await getPosts({
        page: page.value,
        pageSize: 12,
        tagId: tagId.value || undefined,
        categoryId: categoryId.value || undefined,
      })
    }
  } catch {
    result.value = null
  } finally {
    loading.value = false
  }
}

async function resolveFilterName() {
  filterName.value = ''
  try {
    if (tagId.value) {
      const tag = (await getTags()).find((t) => t.id === tagId.value)
      if (tag) filterName.value = `标签：${tag.name}`
    } else if (categoryId.value) {
      const cat = (await getCategories()).find((c) => c.id === categoryId.value)
      if (cat) filterName.value = `分类：${cat.name}`
    }
  } catch {
    /* 名称解析失败不阻塞列表 */
  }
}

function onPageChange(p: number) {
  router.push({ query: { ...route.query, page: p > 1 ? p : undefined } })
}

watch([keyword, tagId, categoryId], () => {
  resolveFilterName()
  load()
})
watch(page, load)

onMounted(() => {
  resolveFilterName()
  load()
})
</script>

<template>
  <div class="post-list-view">
    <header class="page-header">
      <div class="aurora-blobs" />
      <p class="eyebrow">POSTS</p>
      <h1 class="page-title gradient-text">{{ headerTitle }}</h1>
      <p v-if="result" class="page-sub">共 {{ result.total }} 篇文章</p>
    </header>

    <div class="container list-container">
      <PostCardList :result="result" :loading="loading" @page-change="onPageChange" />
    </div>
  </div>
</template>

<style scoped>
.post-list-view {
  padding-top: 64px;
  min-height: 70vh;
}

.page-header {
  position: relative;
  padding: var(--space-16) var(--space-6) var(--space-8);
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
  font-size: 38px;
}

.page-sub {
  position: relative;
  margin-top: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-muted);
}

.list-container {
  padding-bottom: var(--space-16);
}
</style>
