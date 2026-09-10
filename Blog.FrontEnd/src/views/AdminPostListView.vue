<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { deletePost, getPostDetailReadonly, getPosts, publishPost } from '@/api/posts'
import type { PagedResult, PostListItemDto } from '@/types'

const PAGE_SIZE = 20

const items = ref<PostListItemDto[]>([])
const total = ref(0)
const page = ref(1)
const totalPages = ref(1)
const loading = ref(false)
const errorMsg = ref('')
const busyId = ref<string | null>(null)
/** 移动端卡片展开状态（桌面端无意义） */
const expandedId = ref<string | null>(null)

async function load(targetPage = page.value) {
  loading.value = true
  errorMsg.value = ''
  try {
    const res: PagedResult<PostListItemDto> = await getPosts({
      page: targetPage,
      pageSize: PAGE_SIZE,
      includeUnpublished: true,
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

function isDraft(p: PostListItemDto) {
  return !p.publishedAt
}

/** 说明：文章详情接口每次调用会累计浏览量，管理端仅用它取 version 做乐观锁 */
async function withVersion<T>(p: PostListItemDto, fn: (version: number) => Promise<T>) {
  busyId.value = p.id
  errorMsg.value = ''
  try {
    // 用只读端点取 version，避免「取一次就 +1」污染浏览量
    const detail = await getPostDetailReadonly(p.id)
    await fn(detail.version)
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    busyId.value = null
  }
}

function togglePublish(p: PostListItemDto) {
  return withVersion(p, (version) => publishPost(p.id, version, isDraft(p)))
}

function confirmDelete(p: PostListItemDto) {
  if (!window.confirm(`确认删除「${p.title}」？此为软删除，文章将不再出现在任何列表。`)) return
  return withVersion(p, (version) => deletePost(p.id, version))
}

function toggleExpand(p: PostListItemDto) {
  expandedId.value = expandedId.value === p.id ? null : p.id
}

const hasAny = computed(() => items.value.length > 0)
const canPrev = computed(() => page.value > 1)
const canNext = computed(() => page.value < totalPages.value)
</script>

<template>
  <section class="admin-posts">
    <div class="toolbar">
      <span class="toolbar-info">共 {{ total }} 篇（含草稿）</span>
      <RouterLink to="/admin/posts/new" class="btn-primary">+ 新建文章</RouterLink>
    </div>

    <p v-if="errorMsg" class="err-banner">{{ errorMsg }}</p>

    <div v-if="loading" class="table skeleton">
      <div v-for="n in 4" :key="n" class="row skel-row">
        <span class="skel w-40" />
        <span class="skel w-16" />
        <span class="skel w-24" />
      </div>
    </div>

    <div v-else-if="hasAny" class="table">
      <div class="row head-row">
        <span class="col-title">标题</span>
        <span class="col-cat">分类</span>
        <span class="col-tag">标签</span>
        <span class="col-status">状态</span>
        <span class="col-time">时间</span>
        <span class="col-act">操作</span>
      </div>

      <div
        v-for="p in items"
        :key="p.id"
        class="row"
        :class="{ expanded: expandedId === p.id }"
      >
        <span class="col-title" :data-label="'标题'" :title="p.title" @click="toggleExpand(p)">
          <span class="title-text">{{ p.title }}</span>
          <button class="expand-btn" :aria-expanded="expandedId === p.id" @click.stop="toggleExpand(p)">
            {{ expandedId === p.id ? '收起' : '详情' }}
          </button>
        </span>

        <span class="col-cat" data-label="分类">{{ p.categoryName || '—' }}</span>

        <span class="col-tag" data-label="标签">
          <span v-for="t in p.tags" :key="t.id" class="tag-badge">{{ t.name }}</span>
          <span v-if="!p.tags.length" class="muted">—</span>
        </span>

        <span class="col-status" data-label="状态">
          <span v-if="isDraft(p)" class="badge draft">草稿</span>
          <span v-else class="badge pub">已发布</span>
        </span>

        <span class="col-time muted" data-label="时间">
          {{ fmtDate(p.publishedAt) || '未发布' }}
        </span>

        <span class="col-act" data-label="操作">
          <RouterLink class="link" :to="`/admin/posts/${p.id}/edit`">编辑</RouterLink>
          <button class="link" :disabled="busyId === p.id" @click="togglePublish(p)">
            {{ isDraft(p) ? '发布' : '下架' }}
          </button>
          <button class="link danger" :disabled="busyId === p.id" @click="confirmDelete(p)">删除</button>
        </span>
      </div>
    </div>

    <div v-else class="empty">
      <p>还没有任何文章</p>
      <RouterLink to="/admin/posts/new" class="btn-primary">写第一篇</RouterLink>
    </div>

    <nav v-if="hasAny && totalPages > 1" class="pager">
      <button class="btn-ghost" :disabled="!canPrev || loading" @click="load(page - 1)">上一页</button>
      <span class="pager-info">第 {{ page }} / {{ totalPages }} 页</span>
      <button class="btn-ghost" :disabled="!canNext || loading" @click="load(page + 1)">下一页</button>
    </nav>
  </section>
</template>

<style scoped>
.admin-posts {
  display: flex;
  flex-direction: column;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
  margin-bottom: var(--space-4);
}
.toolbar-info {
  font: var(--text-body-sm);
  color: var(--text-muted);
}

.muted {
  color: var(--text-muted);
  font: var(--text-caption);
}

.btn-primary {
  display: inline-flex;
  align-items: center;
  padding: var(--space-2) var(--space-4);
  border: none;
  border-radius: var(--radius-sm);
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
  color: #fff;
  font-size: 14px;
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
.btn-ghost:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.err-banner {
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-sm);
  margin-bottom: var(--space-4);
  font: var(--text-body-sm);
}

.empty {
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}
.empty .btn-primary {
  margin-top: var(--space-4);
}

.table {
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.row {
  display: grid;
  grid-template-columns: minmax(0, 2fr) 1fr minmax(0, 1.4fr) 76px 132px 148px;
  gap: var(--space-3);
  padding: var(--space-3) var(--space-4);
  align-items: center;
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
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  color: var(--text-strong);
  font-weight: 600;
}
.title-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* 展开按钮只在窄屏出现（桌面端整行信息已可见） */
.expand-btn {
  display: none;
  flex-shrink: 0;
  border: 1px solid var(--border-default);
  border-radius: var(--radius-xs);
  padding: 1px 6px;
  font: var(--text-caption);
  color: var(--text-muted);
  background: transparent;
  cursor: pointer;
}

.col-tag {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}
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

.col-time {
  font: var(--text-caption);
  white-space: nowrap;
}

.col-act {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
}
.link {
  background: none;
  border: none;
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

/* 骨架屏 */
.skel-row {
  grid-template-columns: 2fr 1fr 1fr;
}
.skel {
  height: 14px;
  border-radius: var(--radius-xs);
  background: linear-gradient(90deg, var(--bg-raised), var(--border-subtle), var(--bg-raised));
  background-size: 200% 100%;
  animation: skel 1.2s ease-in-out infinite;
}
.w-40 {
  width: 40%;
}
.w-24 {
  width: 24%;
}
.w-16 {
  width: 16%;
}
@keyframes skel {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}

.pager {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  justify-content: flex-end;
  margin-top: var(--space-4);
}
.pager-info {
  font: var(--text-caption);
  color: var(--text-muted);
}

/* ---------- 窄屏：表格退化为带字段标签的卡片，避免「草稿 / 生活」失去上下文 ---------- */
@media (max-width: 880px) {
  .head-row {
    display: none;
  }
  .row {
    grid-template-columns: minmax(0, 1fr) auto;
    gap: var(--space-2) var(--space-4);
    padding: var(--space-4);
    align-items: center;
  }
  .col-title {
    grid-column: 1 / -1;
    font-size: 15px;
  }
  .expand-btn {
    display: inline-block;
  }
  /* 默认只显示标题 + 状态，展开后显示其余字段 */
  .col-cat,
  .col-tag,
  .col-time {
    display: none;
  }
  .row.expanded .col-cat,
  .row.expanded .col-tag,
  .row.expanded .col-time {
    display: flex;
    grid-column: 1 / -1;
    align-items: center;
    gap: var(--space-2);
    font: var(--text-body-sm);
  }
  .row.expanded .col-cat,
  .row.expanded .col-tag,
  .row.expanded .col-time {
    justify-content: flex-start;
  }

  .col-cat::before,
  .col-tag::before,
  .col-time::before {
    flex-shrink: 0;
    width: 48px;
    font: var(--text-caption);
    color: var(--text-subtle);
  }
  .col-cat::before {
    content: '分类';
  }
  .col-tag::before {
    content: '标签';
  }
  .col-time::before {
    content: '时间';
  }

  .col-status {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-2);
    grid-column: 1 / -1;
    grid-row: 1;
  }
  .col-status .badge {
    margin-left: auto;
  }
  .col-act {
    grid-column: 1 / -1;
    justify-content: flex-start;
    padding-top: var(--space-2);
    border-top: 1px dashed var(--border-subtle);
    margin-top: var(--space-1);
  }
}
</style>
