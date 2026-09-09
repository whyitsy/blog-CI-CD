<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { deletePost, getPostDetail, getPosts, publishPost } from '@/api/posts'
import type { PagedResult, PostListItemDto } from '@/types'

const router = useRouter()
const items = ref<PostListItemDto[]>([])
const total = ref(0)
const loading = ref(false)
const errorMsg = ref('')
const busyId = ref<string | null>(null)

async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    const res: PagedResult<PostListItemDto> = await getPosts({
      page: 1,
      pageSize: 50,
      includeUnpublished: true,
    })
    items.value = res.items
    total.value = res.totalCount
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function fmtDate(iso: string | null) {
  return iso ? iso.replace('T', ' ').slice(0, 16) : '草稿'
}

function isDraft(p: PostListItemDto) {
  return !p.publishedAt
}

async function togglePublish(p: PostListItemDto) {
  busyId.value = p.id
  try {
    const detail = await getPostDetail(p.id)
    await publishPost(p.id, detail.version, !!isDraft(p))
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    busyId.value = null
  }
}

async function confirmDelete(p: PostListItemDto) {
  if (!window.confirm(`确认删除「${p.title}」？此为软删除。`)) return
  busyId.value = p.id
  try {
    const detail = await getPostDetail(p.id)
    await deletePost(p.id, detail.version)
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '删除失败'
  } finally {
    busyId.value = null
  }
}

function goEdit(id: string) {
  router.push(`/admin/posts/${id}/edit`)
}

const hasAny = computed(() => items.value.length > 0)
</script>

<template>
  <section class="admin-posts container">
    <header class="admin-head">
      <div>
        <h1>文章管理</h1>
        <p class="muted">共 {{ total }} 篇（含草稿）</p>
      </div>
      <div class="head-actions">
        <button class="btn-ghost" @click="router.push('/')">← 返回首页</button>
        <button class="btn-primary" @click="router.push('/admin/posts/new')">+ 新建文章</button>
      </div>
    </header>

    <p v-if="errorMsg" class="err-banner">{{ errorMsg }}</p>

    <div v-if="loading" class="loading">加载中...</div>

    <div v-else-if="hasAny" class="table">
      <div class="row head-row">
        <span class="col-title">标题</span>
        <span class="col-cat">分类</span>
        <span class="col-tag">标签</span>
        <span class="col-status">状态</span>
        <span class="col-time">时间</span>
        <span class="col-act">操作</span>
      </div>
      <div v-for="p in items" :key="p.id" class="row">
        <span class="col-title" :title="p.title">{{ p.title }}</span>
        <span class="col-cat">{{ p.categoryName || '—' }}</span>
        <span class="col-tag">
          <span v-for="t in p.tags" :key="t.id" class="tag-badge">{{ t.name }}</span>
          <span v-if="!p.tags.length" class="muted">—</span>
        </span>
        <span class="col-status">
          <span v-if="isDraft(p)" class="badge draft">草稿</span>
          <span v-else class="badge pub">已发布</span>
        </span>
        <span class="col-time muted">{{ fmtDate(p.publishedAt) }}</span>
        <span class="col-act">
          <button class="link" :disabled="busyId === p.id" @click="goEdit(p.id)">编辑</button>
          <button class="link" :disabled="busyId === p.id" @click="togglePublish(p)">
            {{ isDraft(p) ? '发布' : '下架' }}
          </button>
          <button class="link danger" :disabled="busyId === p.id" @click="confirmDelete(p)">删除</button>
        </span>
      </div>
    </div>

    <div v-else class="empty">
      <p>还没有任何文章</p>
      <button class="btn-primary" @click="router.push('/admin/posts/new')">写第一篇</button>
    </div>
  </section>
</template>

<style scoped>
.admin-posts {
  padding: var(--space-8) var(--space-4);
  max-width: 1100px;
  margin: 0 auto;
}
.admin-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  margin-bottom: var(--space-6);
  flex-wrap: wrap;
}
.admin-head h1 {
  font: var(--text-h1);
  color: var(--text-strong);
  margin: 0;
}
.muted { color: var(--text-muted); font: var(--text-caption); margin: 4px 0 0; }
.head-actions { display: flex; gap: var(--space-3); }

.btn-primary {
  padding: var(--space-2) var(--space-4);
  border: none;
  border-radius: var(--radius-sm);
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
  color: #fff;
  font-weight: 600;
  cursor: pointer;
}
.btn-ghost {
  padding: var(--space-2) var(--space-4);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-default);
  cursor: pointer;
}

.err-banner {
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
  padding: var(--space-3);
  border-radius: var(--radius-sm);
  margin-bottom: var(--space-4);
}

.loading, .empty {
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}
.empty .btn-primary { margin-top: var(--space-4); }

.table {
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  overflow: hidden;
}
.row {
  display: grid;
  grid-template-columns: 2fr 1fr 1.5fr 0.8fr 1.2fr 1.6fr;
  gap: var(--space-3);
  padding: var(--space-3) var(--space-4);
  align-items: center;
  border-bottom: 1px solid var(--border-subtle);
}
.row:last-child { border-bottom: none; }
.head-row {
  background: var(--bg-raised);
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}
.col-title { color: var(--text-strong); font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.col-tag { display: flex; gap: 4px; flex-wrap: wrap; }
.tag-badge {
  font: var(--text-caption);
  padding: 2px 8px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--brand-500) 15%, transparent);
  color: var(--brand-500);
}
.badge {
  font: var(--text-caption);
  padding: 2px 8px;
  border-radius: 999px;
}
.badge.draft { background: color-mix(in srgb, #d97706 18%, transparent); color: #d97706; }
.badge.pub { background: color-mix(in srgb, #16a34a 18%, transparent); color: #16a34a; }
.col-time { font: var(--text-caption); }
.col-act { display: flex; gap: var(--space-2); }
.link {
  background: none;
  border: none;
  padding: 0;
  font: inherit;
  color: var(--brand-500);
  cursor: pointer;
}
.link:hover { text-decoration: underline; }
.link.danger { color: #e35151; }
.link:disabled { opacity: 0.5; cursor: not-allowed; }

@media (max-width: 880px) {
  .row {
    grid-template-columns: 1fr 1fr;
    grid-auto-rows: auto;
  }
  .head-row { display: none; }
}
</style>