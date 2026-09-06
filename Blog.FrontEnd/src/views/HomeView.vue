<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import HeroSection from '@/components/hero/HeroSection.vue'
import PostCardList from '@/components/post/PostCardList.vue'
import { getPosts } from '@/api/posts'
import type { PagedResult, PostListItemDto } from '@/types'

const route = useRoute()
const router = useRouter()

const result = ref<PagedResult<PostListItemDto> | null>(null)
const loading = ref(true)

async function load(page: number) {
  loading.value = true
  try {
    result.value = await getPosts({ page, pageSize: 12 })
  } catch {
    result.value = null
  } finally {
    loading.value = false
  }
}

function onPageChange(page: number) {
  router.push({ query: page > 1 ? { page } : {} })
  document.getElementById('post-list')?.scrollIntoView({ behavior: 'smooth' })
}

watch(
  () => route.query.page,
  (p) => load(Math.max(1, Number(p) || 1)),
)

onMounted(() => load(Math.max(1, Number(route.query.page) || 1)))
</script>

<template>
  <div class="home-view">
    <HeroSection />

    <section id="post-list" class="post-section container">
      <header class="section-header">
        <h2 class="section-title">最新文章</h2>
        <p class="section-sub">写下来的东西，才算真正想清楚。</p>
      </header>

      <PostCardList :result="result" :loading="loading" @page-change="onPageChange" />
    </section>
  </div>
</template>

<style scoped>
.post-section {
  padding-top: var(--space-16);
  padding-bottom: var(--space-16);
}

.section-header {
  margin-bottom: var(--space-8);
  text-align: center;
}

.section-title {
  font: var(--text-h2);
  color: var(--text-strong);
}

.section-sub {
  margin-top: var(--space-2);
  font: var(--text-body-sm);
  color: var(--text-subtle);
}
</style>
