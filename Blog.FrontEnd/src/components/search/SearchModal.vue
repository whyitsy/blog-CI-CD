<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSearchStore } from '@/stores/app'
import { searchPosts } from '@/api/posts'
import type { PostListItemDto } from '@/types'

const search = useSearchStore()
const router = useRouter()

const keyword = ref('')
const results = ref<PostListItemDto[]>([])
const loading = ref(false)
const searched = ref(false)
const inputRef = ref<HTMLInputElement>()

let debounceTimer = 0

watch(
  () => search.open,
  async (open) => {
    if (open) {
      keyword.value = ''
      results.value = []
      searched.value = false
      await nextTick()
      inputRef.value?.focus()
      document.body.style.overflow = 'hidden'
    } else {
      document.body.style.overflow = ''
    }
  },
)

watch(keyword, (kw) => {
  clearTimeout(debounceTimer)
  if (!kw.trim()) {
    results.value = []
    searched.value = false
    return
  }
  // 300ms 防抖
  debounceTimer = window.setTimeout(() => doSearch(kw.trim()), 300)
})

async function doSearch(kw: string) {
  loading.value = true
  try {
    const res = await searchPosts(kw, 1, 10)
    results.value = res.items
    searched.value = true
  } catch {
    results.value = []
    searched.value = true
  } finally {
    loading.value = false
  }
}

function goPost(id: string) {
  search.hide()
  router.push(`/post/${id}`)
}

function highlight(text: string): string {
  const kw = keyword.value.trim()
  if (!kw) return escapeHtml(text)
  const escaped = escapeHtml(text)
  const kwEscaped = escapeHtml(kw).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return escaped.replace(new RegExp(`(${kwEscaped})`, 'gi'), '<mark>$1</mark>')
}

function escapeHtml(s: string) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function formatDate(iso: string | null) {
  if (!iso) return ''
  return new Date(iso).toLocaleDateString('zh-CN')
}
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div v-if="search.open" class="search-overlay" @click.self="search.hide()">
        <div class="search-modal">
          <div class="search-input-row">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <circle cx="11" cy="11" r="7" />
              <path d="m20 20-3.5-3.5" />
            </svg>
            <input
              ref="inputRef"
              v-model="keyword"
              class="search-input"
              type="text"
              placeholder="搜索文章标题、内容…"
              @keydown.esc="search.hide()"
            />
            <button class="close-btn" aria-label="关闭" @click="search.hide()">ESC</button>
          </div>

          <div class="search-results">
            <p v-if="loading" class="hint">搜索中…</p>
            <p v-else-if="searched && results.length === 0" class="hint">没有找到相关文章</p>
            <p v-else-if="!keyword.trim()" class="hint">输入关键词，模糊匹配全部文章</p>

            <button v-for="post in results" :key="post.id" class="result-item" @click="goPost(post.id)">
              <span class="result-title" v-html="highlight(post.title)" />
              <span class="result-summary" v-html="highlight(post.summary)" />
              <span class="result-date">{{ formatDate(post.publishedAt) }}</span>
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.search-overlay {
  position: fixed;
  inset: 0;
  z-index: 200;
  display: flex;
  justify-content: center;
  padding: 12vh var(--space-4) 0;
  background: rgba(14, 15, 19, 0.6);
  backdrop-filter: blur(4px);
}

.search-modal {
  width: 100%;
  max-width: 600px;
  height: fit-content;
  max-height: 70vh;
  display: flex;
  flex-direction: column;
  background: var(--bg-surface);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-lg), var(--glow-purple);
  overflow: hidden;
}

.search-input-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-4) var(--space-5);
  color: var(--text-subtle);
  border-bottom: 1px solid var(--border-subtle);
}

.search-input {
  flex: 1;
  border: none;
  outline: none;
  background: transparent;
  font-size: 16px;
  color: var(--text-strong);
}

.search-input::placeholder {
  color: var(--text-subtle);
}

.close-btn {
  padding: 2px var(--space-2);
  font: var(--text-caption);
  color: var(--text-subtle);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-xs);
}

.search-results {
  overflow-y: auto;
  padding: var(--space-2);
}

.hint {
  padding: var(--space-8);
  text-align: center;
  font: var(--text-body-sm);
  color: var(--text-subtle);
}

.result-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 100%;
  padding: var(--space-3) var(--space-4);
  text-align: left;
  border-radius: var(--radius-md);
  transition: background var(--transition-fast);
}

.result-item:hover {
  background: var(--bg-raised);
}

.result-title {
  font-weight: 600;
  color: var(--text-strong);
}

.result-summary {
  font: var(--text-body-sm);
  color: var(--text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.result-date {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.result-item :deep(mark) {
  color: var(--brand-500);
  background: transparent;
}
</style>
