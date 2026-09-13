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
  } catch (e) {
    // 加载失败按「空列表」呈现，与专栏/分类/归档等公开页保持一致：
    // 技术细节只进控制台，页面不暴露 HTTP 状态码，也不弹错误横幅。
    //
    // ⚠️ 这个 catch 不是可有可无的：只写 try/finally 的话 UI 看起来完全正常
    //    （loading 置回 false、列表仍为空 → 显示「暂无标签」），
    //    但 Promise 的 rejection **没有人接**，控制台会冒出 Uncaught (in promise)。
    //    将来一旦接入全局的 unhandledrejection 上报，它就会变成一条假告警。
    //    （这个不一致由 tools/e2e 的 07-public-lists-failure 发现并守护）
    tags.value = []
    console.error('[tags] 加载标签列表失败：', e)
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
