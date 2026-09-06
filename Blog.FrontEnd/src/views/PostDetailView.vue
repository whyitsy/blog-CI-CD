<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { marked } from 'marked'
import DOMPurify from 'dompurify'
import { getPostDetail } from '@/api/posts'
import type { PostDetailDto } from '@/types'
import GiscusComments from '@/components/common/GiscusComments.vue'

const route = useRoute()
const post = ref<PostDetailDto | null>(null)
const loading = ref(true)
const notFound = ref(false)

interface TocItem {
  id: string
  text: string
  level: number
}
const toc = ref<TocItem[]>([])
const activeHeading = ref('')
const readProgress = ref(0)

const renderedContent = computed(() => {
  if (!post.value) return ''
  const raw = marked.parse(post.value.content, { async: false }) as string
  return DOMPurify.sanitize(raw, { ADD_ATTR: ['id'] })
})

async function load(id: string) {
  loading.value = true
  notFound.value = false
  post.value = null
  try {
    post.value = await getPostDetail(id)
    document.title = `${post.value.title} - kky's blog`
    buildToc()
  } catch {
    notFound.value = true
  } finally {
    loading.value = false
  }
}

/** 从 markdown 提取 h2/h3 生成目录 */
function buildToc() {
  if (!post.value) return
  const items: TocItem[] = []
  const re = /^(#{2,3})\s+(.+)$/gm
  let match
  let idx = 0
  while ((match = re.exec(post.value.content)) !== null) {
    items.push({
      id: `heading-${idx++}`,
      text: match[2]!.replace(/[*`]/g, ''),
      level: match[1]!.length,
    })
  }
  toc.value = items
}

/** 渲染后为标题补充 id（与 toc 对应） */
function patchHeadingIds() {
  const article = document.querySelector('.article-body')
  if (!article) return
  article.querySelectorAll('h2, h3').forEach((h, i) => {
    if (!h.id) h.id = `heading-${i}`
  })
}

function onScroll() {
  // 阅读进度
  const el = document.documentElement
  const total = el.scrollHeight - el.clientHeight
  readProgress.value = total > 0 ? Math.min(100, Math.round((window.scrollY / total) * 100)) : 0

  // 目录高亮
  const headings = document.querySelectorAll('.article-body h2, .article-body h3')
  let current = ''
  headings.forEach((h) => {
    if (h.getBoundingClientRect().top < 120) current = h.id
  })
  activeHeading.value = current
}

function scrollToHeading(id: string) {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' })
}

watch(post, () => setTimeout(patchHeadingIds, 0))
watch(() => route.params.id, (id) => id && load(String(id)))

onMounted(() => {
  load(String(route.params.id))
  window.addEventListener('scroll', onScroll, { passive: true })
})
onBeforeUnmount(() => window.removeEventListener('scroll', onScroll))

function formatDate(iso: string | null) {
  if (!iso) return ''
  return new Date(iso).toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' })
}

const readMinutes = computed(() => (post.value ? Math.max(1, Math.round(post.value.wordCount / 400)) : 0))
</script>

<template>
  <div class="post-detail-view">
    <!-- 阅读进度条 -->
    <div class="progress-bar" :style="{ width: `${readProgress}%` }" />

    <div v-if="loading" class="container detail-loading">
      <div class="dl-hero shimmer" />
      <div class="dl-line shimmer w-60" />
      <div class="dl-line shimmer" />
      <div class="dl-line shimmer w-80" />
    </div>

    <div v-else-if="notFound" class="container not-found">
      <p class="nf-icon">🔍</p>
      <h1 class="nf-title">文章不存在</h1>
      <RouterLink to="/" class="nf-link">返回首页</RouterLink>
    </div>

    <template v-else-if="post">
      <!-- 封面头图 -->
      <header class="post-hero">
        <div
          class="post-hero-bg"
          :style="post.coverImage ? { backgroundImage: `url(${post.coverImage})` } : undefined"
          :class="{ 'no-cover': !post.coverImage }"
        />
        <div class="post-hero-mask" />
        <div class="container post-hero-content">
          <div class="post-badges">
            <RouterLink
              v-if="post.categoryId"
              :to="{ path: '/posts', query: { categoryId: post.categoryId } }"
              class="category-badge"
            >
              {{ post.categoryName }}
            </RouterLink>
            <RouterLink
              v-for="tag in post.tags"
              :key="tag.id"
              :to="{ path: '/posts', query: { tagId: tag.id } }"
              class="tag-badge"
            >
              {{ tag.name }}
            </RouterLink>
          </div>
          <h1 class="post-title">{{ post.title }}</h1>
          <div class="post-meta">
            <img :src="post.authorAvatar" :alt="post.authorName" class="meta-avatar" />
            <span class="meta-author">{{ post.authorName }}</span>
            <span class="meta-sep">·</span>
            <time>{{ formatDate(post.publishedAt) }}</time>
            <span class="meta-sep">·</span>
            <span>{{ readMinutes }} 分钟阅读</span>
            <span class="meta-sep">·</span>
            <span>{{ post.viewCount }} 次浏览</span>
          </div>
        </div>
      </header>

      <!-- 正文 + 侧栏 -->
      <div class="container detail-layout">
        <article class="article-body" v-html="renderedContent" />

        <aside v-if="toc.length" class="detail-sidebar">
          <div class="sidebar-card card">
            <h4 class="sidebar-title">目录</h4>
            <nav class="toc">
              <a
                v-for="item in toc"
                :key="item.id"
                :href="`#${item.id}`"
                class="toc-link"
                :class="{ active: activeHeading === item.id, 'toc-h3': item.level === 3 }"
                @click.prevent="scrollToHeading(item.id)"
              >
                {{ item.text }}
              </a>
            </nav>
          </div>

          <div class="sidebar-card card">
            <h4 class="sidebar-title">阅读进度</h4>
            <p class="progress-num">{{ readProgress }}%</p>
            <div class="progress-track">
              <div class="progress-fill" :style="{ width: `${readProgress}%` }" />
            </div>
          </div>
        </aside>
      </div>

      <!-- 评论 -->
      <div class="container comments-section">
        <h3 class="comments-title">评论</h3>
        <GiscusComments />
      </div>
    </template>
  </div>
</template>

<style scoped>
.post-detail-view {
  padding-top: 64px;
}

.progress-bar {
  position: fixed;
  top: 64px;
  left: 0;
  height: 2px;
  z-index: 99;
  background: linear-gradient(90deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  transition: width 100ms linear;
}

/* ---------- 头图 ---------- */
.post-hero {
  position: relative;
  min-height: 340px;
  display: flex;
  align-items: flex-end;
  overflow: hidden;
}

.post-hero-bg {
  position: absolute;
  inset: 0;
  background-size: cover;
  background-position: center;
}

.post-hero-bg.no-cover {
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  opacity: 0.75;
}

.post-hero-mask {
  position: absolute;
  inset: 0;
  background: linear-gradient(180deg, rgba(19, 20, 26, 0.3), var(--bg-canvas) 96%);
}

[data-theme='light'] .post-hero-mask {
  background: linear-gradient(180deg, rgba(241, 243, 249, 0.2), var(--bg-canvas) 96%);
}

.post-hero-content {
  position: relative;
  z-index: 2;
  padding-top: var(--space-16);
  padding-bottom: var(--space-8);
}

.post-badges {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  margin-bottom: var(--space-4);
}

.category-badge {
  padding: 3px var(--space-3);
  font: var(--text-caption);
  font-weight: 600;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-mid), var(--gradient-end));
  border-radius: var(--radius-xs);
}

.post-title {
  font: var(--text-h1);
  font-size: 36px;
  color: var(--text-strong);
  max-width: 800px;
  margin-bottom: var(--space-4);
}

.post-meta {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font: var(--text-body-sm);
  color: var(--text-muted);
  flex-wrap: wrap;
}

.meta-avatar {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  object-fit: cover;
  background: var(--bg-raised);
}

.meta-author {
  font-weight: 600;
  color: var(--text-default);
}

.meta-sep {
  color: var(--text-disabled);
}

/* ---------- 双栏 ---------- */
.detail-layout {
  display: grid;
  grid-template-columns: minmax(0, var(--size-prose)) var(--size-sidebar);
  gap: var(--space-10);
  justify-content: center;
  padding-top: var(--space-8);
}

@media (max-width: 1023px) {
  .detail-layout {
    grid-template-columns: minmax(0, 1fr);
  }
  .detail-sidebar {
    display: none;
  }
}

.detail-sidebar {
  position: sticky;
  top: 88px;
  align-self: start;
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.sidebar-card {
  padding: var(--space-5);
}

.sidebar-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-strong);
  margin-bottom: var(--space-3);
}

.toc {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  max-height: 320px;
  overflow-y: auto;
}

.toc-link {
  font: var(--text-body-sm);
  color: var(--text-muted);
  border-left: 2px solid var(--border-subtle);
  padding-left: var(--space-3);
  transition: color var(--transition-fast), border-color var(--transition-fast);
}

.toc-link.toc-h3 {
  padding-left: var(--space-6);
  font-size: 13px;
}

.toc-link:hover,
.toc-link.active {
  color: var(--brand-500);
  border-left-color: var(--brand-500);
}

.progress-num {
  font-size: 26px;
  font-weight: 700;
  color: var(--text-strong);
  margin-bottom: var(--space-2);
}

.progress-track {
  height: 6px;
  border-radius: 3px;
  background: var(--bg-sunken);
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  border-radius: 3px;
  background: linear-gradient(90deg, var(--gradient-start), var(--gradient-end));
  transition: width 100ms linear;
}

/* ---------- 正文排版 ---------- */
.article-body {
  font-size: 16px;
  line-height: 1.75;
  color: var(--text-default);
  overflow-wrap: break-word;
}

.article-body :deep(h2) {
  font: var(--text-h2);
  font-size: 24px;
  color: var(--text-strong);
  margin: var(--space-10) 0 var(--space-4);
  scroll-margin-top: 88px;
}

.article-body :deep(h3) {
  font: var(--text-h3);
  font-size: 19px;
  color: var(--text-strong);
  margin: var(--space-8) 0 var(--space-3);
  scroll-margin-top: 88px;
}

.article-body :deep(p) {
  margin-bottom: var(--space-4);
}

.article-body :deep(a) {
  color: var(--brand-500);
  border-bottom: 1px dashed color-mix(in srgb, var(--brand-500) 50%, transparent);
}

.article-body :deep(strong) {
  color: var(--text-strong);
  font-weight: 600;
}

.article-body :deep(ul),
.article-body :deep(ol) {
  margin: 0 0 var(--space-4) var(--space-6);
}

.article-body :deep(ul) {
  list-style: disc;
}

.article-body :deep(ol) {
  list-style: decimal;
}

.article-body :deep(li) {
  margin-bottom: var(--space-2);
}

.article-body :deep(blockquote) {
  margin: 0 0 var(--space-4);
  padding: var(--space-3) var(--space-4);
  border-left: 3px solid var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 6%, transparent);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
  color: var(--text-muted);
}

.article-body :deep(code) {
  font-family: var(--font-mono);
  font-size: 0.88em;
  padding: 2px 6px;
  background: var(--bg-sunken);
  border-radius: var(--radius-xs);
}

.article-body :deep(pre) {
  margin-bottom: var(--space-4);
  padding: var(--space-4);
  background: var(--bg-sunken);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  overflow-x: auto;
}

.article-body :deep(pre code) {
  padding: 0;
  background: none;
  font-size: 13px;
  line-height: 1.6;
}

.article-body :deep(img) {
  margin: var(--space-4) auto;
  border-radius: var(--radius-md);
}

.article-body :deep(hr) {
  margin: var(--space-8) 0;
  border: none;
  height: 1px;
  background: var(--border-subtle);
}

.article-body :deep(table) {
  width: 100%;
  margin-bottom: var(--space-4);
  border-collapse: collapse;
  font-size: 14px;
}

.article-body :deep(th),
.article-body :deep(td) {
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--border-subtle);
}

.article-body :deep(th) {
  background: var(--bg-raised);
  color: var(--text-strong);
}

/* ---------- 评论 ---------- */
.comments-section {
  max-width: var(--size-prose);
  padding-top: var(--space-10);
  padding-bottom: var(--space-16);
}

.comments-title {
  font: var(--text-h3);
  color: var(--text-strong);
}

/* ---------- 加载与 404 ---------- */
.detail-loading {
  padding-top: var(--space-16);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  max-width: var(--size-prose);
}

.dl-hero {
  height: 280px;
  border-radius: var(--radius-lg);
}

.dl-line {
  height: 18px;
  border-radius: var(--radius-xs);
}

.w-60 { width: 60%; }
.w-80 { width: 80%; }

.shimmer {
  background: linear-gradient(90deg, var(--bg-raised) 25%, var(--border-subtle) 50%, var(--bg-raised) 75%);
  background-size: 200% 100%;
  animation: shimmer 1.4s infinite;
}

@keyframes shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

.not-found {
  padding: var(--space-24) 0;
  text-align: center;
}

.nf-icon {
  font-size: 48px;
  margin-bottom: var(--space-4);
}

.nf-title {
  font: var(--text-h2);
  color: var(--text-strong);
  margin-bottom: var(--space-6);
}

.nf-link {
  padding: var(--space-3) var(--space-6);
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
  border-radius: var(--radius-sm);
}
</style>
