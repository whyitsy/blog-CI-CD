<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { deletePost, getPostDetailReadonly, getPosts, publishPost } from '@/api/posts'
import { useAuthStore } from '@/stores/auth'
import type { PagedResult, PostListItemDto } from '@/types'

const auth = useAuthStore()

const items = ref<PostListItemDto[]>([])
const total = ref(0)
const page = ref(1)
const totalPages = ref(1)
const loading = ref(false)
const errorMsg = ref('')
const okMsg = ref('')
const busyId = ref<string | null>(null)
/** 是否只看草稿 */
const onlyDrafts = ref(false)

const PAGE_SIZE = 20

const visibleItems = computed(() =>
  onlyDrafts.value ? items.value.filter((p) => !p.publishedAt) : items.value,
)
const draftCount = computed(() => items.value.filter((p) => !p.publishedAt).length)

async function load(targetPage = page.value) {
  loading.value = true
  errorMsg.value = ''
  try {
    // mine=true：后端按当前登录账号的 CreatedByUserId 过滤，只看自己创建的
    const res: PagedResult<PostListItemDto> = await getPosts({
      page: targetPage,
      pageSize: PAGE_SIZE,
      includeUnpublished: true,
      mine: true,
    })
    items.value = res.items
    total.value = res.total
    page.value = res.page
    totalPages.value = res.totalPages || 1
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(() => load(1))

function fmtDate(iso: string | null) {
  return iso ? iso.replace('T', ' ').slice(0, 16) : ''
}

async function withVersion<T>(p: PostListItemDto, fn: (version: number) => Promise<T>, okText: string) {
  busyId.value = p.id
  errorMsg.value = ''
  okMsg.value = ''
  try {
    // 用**只读**详情取 version，避免污染浏览量
    const detail = await getPostDetailReadonly(p.id)
    await fn(detail.version)
    okMsg.value = okText
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    busyId.value = null
  }
}

function togglePublish(p: PostListItemDto) {
  const willPublish = !p.publishedAt
  return withVersion(p, (v) => publishPost(p.id, v, willPublish), willPublish ? '已发布' : '已下架')
}

function confirmDelete(p: PostListItemDto) {
  if (!window.confirm(`确认删除「${p.title}」？此为软删除，文章将不再出现在任何列表。`)) return
  return withVersion(p, (v) => deletePost(p.id, v), '已删除')
}

function canPrev() {
  return page.value > 1
}
function canNext() {
  return page.value < totalPages.value
}
</script>

<template>
  <section class="my-posts">
    <header class="page-head">
      <div>
        <h1>我的文章</h1>
        <p class="muted">
          共 {{ total }} 篇，其中草稿 {{ draftCount }} 篇
          <template v-if="auth.user?.authorName">
            · 署名「{{ auth.user.authorName }}」
          </template>
        </p>
      </div>
      <div class="head-actions">
        <label class="filter">
          <input v-model="onlyDrafts" type="checkbox" />
          <span>只看草稿</span>
        </label>
        <RouterLink to="/admin/posts/new" class="btn-primary">+ 写文章</RouterLink>
      </div>
    </header>

    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <div v-if="loading" class="muted-block">加载中...</div>

    <div v-else-if="visibleItems.length" class="list card">
      <div class="row head-row">
        <span>标题</span>
        <span>状态</span>
        <span>时间</span>
        <span class="right">操作</span>
      </div>
      <div v-for="p in visibleItems" :key="p.id" class="row">
        <span class="col-title">
          <RouterLink :to="`/post/${p.id}`" class="title-link" :title="p.title">{{ p.title }}</RouterLink>
        </span>
        <span>
          <span v-if="!p.publishedAt" class="badge draft">草稿</span>
          <span v-else class="badge pub">已发布</span>
        </span>
        <span class="muted">{{ fmtDate(p.publishedAt) || '未发布' }}</span>
        <span class="right actions">
          <RouterLink class="link" :to="`/admin/posts/${p.id}/edit`">编辑</RouterLink>
          <button class="link" :disabled="busyId === p.id" @click="togglePublish(p)">
            {{ p.publishedAt ? '下架' : '发布' }}
          </button>
          <button class="link danger" :disabled="busyId === p.id" @click="confirmDelete(p)">删除</button>
        </span>
      </div>
    </div>

    <div v-else class="muted-block">
      <p>{{ onlyDrafts ? '没有草稿' : '你还没有写过文章' }}</p>
      <RouterLink to="/admin/posts/new" class="btn-primary">写第一篇</RouterLink>
    </div>

    <nav v-if="totalPages > 1" class="pager">
      <button class="btn-ghost" :disabled="!canPrev() || loading" @click="load(page - 1)">上一页</button>
      <span class="pager-info">第 {{ page }} / {{ totalPages }} 页</span>
      <button class="btn-ghost" :disabled="!canNext() || loading" @click="load(page + 1)">下一页</button>
    </nav>
  </section>
</template>

<style scoped>
.my-posts {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.page-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.page-head h1 {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}
.muted {
  color: var(--text-muted);
  font: var(--text-caption);
  margin: 4px 0 0;
}

.head-actions {
  display: flex;
  align-items: center;
  gap: var(--space-4);
}
.filter {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font: var(--text-caption);
  color: var(--text-muted);
  cursor: pointer;
}

.banner {
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-sm);
  font: var(--text-body-sm);
}
.banner.err {
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
}
.banner.ok {
  background: color-mix(in srgb, #16a34a 14%, transparent);
  color: #16a34a;
}

.muted-block {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-4);
  padding: var(--space-12) 0;
  color: var(--text-muted);
}

.list {
  overflow: hidden;
}
.row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 90px 150px 190px;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}
.row:last-child {
  border-bottom: none;
}
.head-row {
  background: var(--bg-raised);
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}

.col-title {
  min-width: 0;
}
.title-link {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 600;
  color: var(--text-strong);
}
.title-link:hover {
  color: var(--brand-500);
}

.badge {
  font: var(--text-caption);
  padding: 2px 8px;
  border-radius: 999px;
  white-space: nowrap;
}
.badge.draft {
  background: color-mix(in srgb, #d97706 18%, transparent);
  color: #d97706;
}
.badge.pub {
  background: color-mix(in srgb, #16a34a 18%, transparent);
  color: #16a34a;
}

.right {
  text-align: right;
}
.actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
}

.btn-primary {
  display: inline-flex;
  align-items: center;
  padding: var(--space-2) var(--space-4);
  border: none;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  color: #fff;
  cursor: pointer;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}
.btn-ghost {
  padding: var(--space-2) var(--space-4);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-default);
  cursor: pointer;
}
.btn-ghost:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.link {
  border: none;
  background: none;
  padding: 0;
  font-size: 13px;
  color: var(--brand-500);
  cursor: pointer;
  white-space: nowrap;
}
.link:hover {
  text-decoration: underline;
}
.link.danger {
  color: #e35151;
}
.link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.pager {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  justify-content: flex-end;
}
.pager-info {
  font: var(--text-caption);
  color: var(--text-muted);
}

@media (max-width: 880px) {
  .head-row {
    display: none;
  }
  .row {
    grid-template-columns: 1fr auto;
    gap: var(--space-2) var(--space-3);
  }
  .col-title {
    grid-column: 1 / -1;
    font-size: 15px;
  }
  .row > span:nth-child(2) {
    justify-self: start;
  }
  .right {
    text-align: left;
  }
  .actions {
    grid-column: 1 / -1;
    justify-content: flex-start;
    padding-top: var(--space-2);
    border-top: 1px dashed var(--border-subtle);
  }
}
</style>
