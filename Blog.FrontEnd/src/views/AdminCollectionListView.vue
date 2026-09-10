<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  createCollection,
  deleteCollection,
  getCollectionById,
  getCollections,
  setCollectionPosts,
  updateCollection,
} from '@/api/collections'
import { getPosts } from '@/api/posts'
import FormSkeleton from '@/components/skeleton/FormSkeleton.vue'
import type { CollectionDto, CollectionPayload, PostListItemDto } from '@/types'

const items = ref<CollectionDto[]>([])
const loading = ref(false)
const busy = ref(false)
const errorMsg = ref('')
const okMsg = ref('')

// ---------------------------------------------------------------- 新建 / 编辑
const showForm = ref(false)
const editingId = ref<string | null>(null)
const form = reactive<CollectionPayload>({
  title: '',
  slug: '',
  description: '',
  coverImage: '',
  sortOrder: 0,
  isPublished: true,
})

// ---------------------------------------------------------------- 文章编排
const managing = ref<CollectionDto | null>(null)
const manageLoading = ref(false)
const allPosts = ref<PostListItemDto[]>([])
const selectedIds = ref<string[]>([])
const manageVersion = ref(1)

const isEditing = computed(() => editingId.value !== null)

async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    items.value = await getCollections(true)
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function flash(msg: string) {
  okMsg.value = msg
  window.setTimeout(() => {
    if (okMsg.value === msg) okMsg.value = ''
  }, 2500)
}

function resetForm() {
  editingId.value = null
  form.title = ''
  form.slug = ''
  form.description = ''
  form.coverImage = ''
  form.sortOrder = items.value.length
  form.isPublished = true
}

function openCreate() {
  resetForm()
  showForm.value = true
}

function openEdit(c: CollectionDto) {
  editingId.value = c.id
  form.title = c.title
  form.slug = c.slug
  form.description = c.description
  form.coverImage = c.coverImage
  form.sortOrder = c.sortOrder
  form.isPublished = c.isPublished
  showForm.value = true
}

/** 标题自动生成 slug 草稿（仅在新建且用户未手改 slug 时） */
function onTitleInput() {
  if (isEditing.value) return
  const ascii = form.title
    .toLowerCase()
    .replace(/[^a-z0-9\s-]/g, '')
    .trim()
    .replace(/\s+/g, '-')
  if (ascii) form.slug = ascii
}

async function onSubmit() {
  errorMsg.value = ''
  if (!form.title.trim()) {
    errorMsg.value = '请填写专栏标题'
    return
  }
  if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(form.slug.trim())) {
    errorMsg.value = '专栏标识只能包含小写字母、数字与中划线（如 my-series）'
    return
  }

  busy.value = true
  try {
    if (isEditing.value && editingId.value) {
      const target = items.value.find((c) => c.id === editingId.value)
      await updateCollection(editingId.value, { ...form, version: target?.version })
      flash('已保存')
    } else {
      await createCollection(form)
      flash('专栏已创建')
    }
    showForm.value = false
    resetForm()
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    busy.value = false
  }
}

async function onDelete(c: CollectionDto) {
  if (!window.confirm(`确认删除专栏「${c.title}」？专栏内的文章本身不会被删除。`)) return
  busy.value = true
  errorMsg.value = ''
  try {
    await deleteCollection(c.id, c.version)
    flash('专栏已删除')
    if (managing.value?.id === c.id) managing.value = null
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '删除失败'
  } finally {
    busy.value = false
  }
}

// ---------------------------------------------------------------- 文章编排
async function openManage(c: CollectionDto) {
  managing.value = c
  manageLoading.value = true
  errorMsg.value = ''
  try {
    const [detail, posts] = await Promise.all([
      getCollectionById(c.id),
      getPosts({ page: 1, pageSize: 100, includeUnpublished: true }),
    ])
    manageVersion.value = detail.version
    selectedIds.value = detail.posts.map((p) => p.id)
    allPosts.value = posts.items
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    manageLoading.value = false
  }
}

function togglePost(id: string) {
  const i = selectedIds.value.indexOf(id)
  if (i >= 0) selectedIds.value.splice(i, 1)
  else selectedIds.value.push(id)
}

function move(index: number, delta: number) {
  const target = index + delta
  if (target < 0 || target >= selectedIds.value.length) return
  const arr = selectedIds.value
  ;[arr[index], arr[target]] = [arr[target], arr[index]]
}

async function saveManage() {
  if (!managing.value) return
  busy.value = true
  errorMsg.value = ''
  try {
    const detail = await setCollectionPosts(managing.value.id, selectedIds.value, manageVersion.value)
    manageVersion.value = detail.version
    flash('专栏文章已保存')
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    busy.value = false
  }
}

function selectedTitle(id: string) {
  return allPosts.value.find((p) => p.id === id)?.title ?? id
}
</script>

<template>
  <section class="admin-collections">
    <div class="toolbar">
      <span class="toolbar-info">共 {{ items.length }} 个专栏</span>
      <button class="btn-primary" @click="showForm ? (showForm = false) : openCreate()">
        {{ showForm ? '收起' : '+ 新建专栏' }}
      </button>
    </div>

    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <!-- 新建 / 编辑表单 -->
    <form v-if="showForm" class="card panel" @submit.prevent="onSubmit">
      <h3>{{ isEditing ? '编辑专栏' : '新建专栏' }}</h3>

      <div class="grid">
        <label class="field">
          <span class="label">标题</span>
          <input
            v-model="form.title"
            class="input"
            maxlength="200"
            placeholder="例如：C# 进阶系列"
            @input="onTitleInput"
          />
          <span class="hint">中文标题不会自动生成 slug，请手动填写下方标识</span>
        </label>

        <label class="field">
          <span class="label">标识（slug，用于 URL）</span>
          <input v-model.trim="form.slug" class="input" maxlength="200" placeholder="csharp-advanced" />
          <span class="hint">仅小写字母、数字、中划线；访问路径 /collections/{slug}</span>
        </label>

        <label class="field">
          <span class="label">排序</span>
          <input v-model.number="form.sortOrder" class="input" type="number" min="0" />
          <span class="hint">越小越靠前</span>
        </label>

        <label class="field">
          <span class="label">封面图 URL</span>
          <input v-model.trim="form.coverImage" class="input" placeholder="留空用渐变占位" />
        </label>
      </div>

      <label class="field">
        <span class="label">简介</span>
        <textarea v-model="form.description" class="input textarea" rows="2" maxlength="500" />
      </label>

      <label class="switch">
        <input v-model="form.isPublished" type="checkbox" />
        <span>{{ form.isPublished ? '已发布（前台可见）' : '未发布（仅管理员可见）' }}</span>
      </label>

      <footer class="panel-actions">
        <button type="button" class="btn-ghost" @click="((showForm = false), resetForm())">取消</button>
        <button type="submit" class="btn-primary" :disabled="busy">
          {{ busy ? '保存中...' : isEditing ? '保存' : '创建' }}
        </button>
      </footer>
    </form>

    <FormSkeleton v-if="loading" :fields="3" />

    <div v-else class="list card">
      <div class="row head-row">
        <span>标题</span>
        <span>标识</span>
        <span>文章数</span>
        <span>排序</span>
        <span>状态</span>
        <span class="right">操作</span>
      </div>

      <div v-for="c in items" :key="c.id" class="row">
        <span class="col-title">{{ c.title }}</span>
        <code class="slug">/{{ c.slug }}</code>
        <span class="muted">{{ c.postCount }} 篇</span>
        <span class="muted">{{ c.sortOrder }}</span>
        <span>
          <span v-if="c.isPublished" class="badge pub">已发布</span>
          <span v-else class="badge draft">未发布</span>
        </span>
        <span class="right actions">
          <button class="link" :disabled="busy" @click="openManage(c)">编排文章</button>
          <button class="link" :disabled="busy" @click="openEdit(c)">编辑</button>
          <button class="link danger" :disabled="busy" @click="onDelete(c)">删除</button>
        </span>
      </div>

      <p v-if="!items.length" class="empty-row">还没有专栏</p>
    </div>

    <!-- 文章编排 -->
    <div v-if="managing" class="card panel manage">
      <h3>编排文章 · {{ managing.title }}</h3>
      <p class="hint">
        勾选属于该专栏的文章，用箭头调整**专栏内阅读顺序**。一篇文章可同时属于多个专栏。
      </p>

      <div v-if="manageLoading" class="muted-block">加载中...</div>

      <div v-else class="manage-grid">
        <div class="pane">
          <h4>可选文章（{{ allPosts.length }}）</h4>
          <label v-for="p in allPosts" :key="p.id" class="opt">
            <input
              type="checkbox"
              :checked="selectedIds.includes(p.id)"
              @change="togglePost(p.id)"
            />
            <span class="opt-title">{{ p.title }}</span>
            <span v-if="!p.publishedAt" class="badge draft">草稿</span>
          </label>
          <p v-if="!allPosts.length" class="muted">还没有文章</p>
        </div>

        <div class="pane">
          <h4>专栏内顺序（{{ selectedIds.length }}）</h4>
          <ol class="ordered">
            <li v-for="(id, i) in selectedIds" :key="id" class="ordered-item">
              <span class="idx">{{ i + 1 }}</span>
              <span class="opt-title">{{ selectedTitle(id) }}</span>
              <span class="order-actions">
                <button class="mini" :disabled="i === 0" @click="move(i, -1)">↑</button>
                <button class="mini" :disabled="i === selectedIds.length - 1" @click="move(i, 1)">↓</button>
                <button class="mini danger" @click="togglePost(id)">×</button>
              </span>
            </li>
          </ol>
          <p v-if="!selectedIds.length" class="muted">左侧勾选文章加入本专栏</p>
        </div>
      </div>

      <footer class="panel-actions">
        <button class="btn-ghost" @click="managing = null">关闭</button>
        <button class="btn-primary" :disabled="busy || manageLoading" @click="saveManage">
          {{ busy ? '保存中...' : '保存编排' }}
        </button>
      </footer>
    </div>
  </section>
</template>

<style scoped>
.admin-collections {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.toolbar-info {
  font: var(--text-body-sm);
  color: var(--text-muted);
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

.panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-6);
}
.panel h3 {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}
.panel h4 {
  font-size: 14px;
  color: var(--text-muted);
  margin: 0 0 var(--space-2);
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--space-4);
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.label {
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}
.hint {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.input {
  width: 100%;
  padding: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-strong);
  background: var(--bg-canvas);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  outline: none;
}
.input:focus {
  border-color: var(--brand-500);
}
.textarea {
  resize: vertical;
  font-family: inherit;
}

.switch {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font: var(--text-caption);
  color: var(--text-muted);
  cursor: pointer;
}

.panel-actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
}

.list {
  overflow: hidden;
}
.row {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) 140px 80px 60px 90px 220px;
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
  color: var(--text-strong);
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.slug {
  font-family: var(--font-mono, monospace);
  font-size: 12px;
  color: var(--brand-500);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.muted {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.badge {
  font: var(--text-caption);
  padding: 2px 8px;
  border-radius: 999px;
  white-space: nowrap;
}
.badge.pub {
  color: #16a34a;
  background: color-mix(in srgb, #16a34a 18%, transparent);
}
.badge.draft {
  color: #d97706;
  background: color-mix(in srgb, #d97706 18%, transparent);
}

.right {
  text-align: right;
}
.actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
  flex-wrap: wrap;
}

.empty-row {
  padding: var(--space-8);
  text-align: center;
  color: var(--text-subtle);
  font: var(--text-body-sm);
}

.muted-block {
  padding: var(--space-8) 0;
  text-align: center;
  color: var(--text-muted);
}

.manage-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-6);
}

.pane {
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  padding: var(--space-4);
  max-height: 420px;
  overflow-y: auto;
}

.opt {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-2) 0;
  cursor: pointer;
  border-bottom: 1px dashed var(--border-subtle);
}
.opt:last-child {
  border-bottom: none;
}
.opt-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font: var(--text-body-sm);
  color: var(--text-default);
}

.ordered {
  list-style: none;
  padding: 0;
  margin: 0;
}
.ordered-item {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-2) 0;
  border-bottom: 1px dashed var(--border-subtle);
}
.ordered-item:last-child {
  border-bottom: none;
}
.idx {
  display: grid;
  place-items: center;
  width: 22px;
  height: 22px;
  flex-shrink: 0;
  border-radius: 50%;
  font: var(--text-caption);
  color: var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 12%, transparent);
}
.order-actions {
  display: flex;
  gap: var(--space-1);
}
.mini {
  width: 24px;
  height: 24px;
  border: 1px solid var(--border-default);
  border-radius: var(--radius-xs);
  background: transparent;
  color: var(--text-muted);
  cursor: pointer;
}
.mini:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.mini.danger {
  color: #e35151;
}

.btn-primary {
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
.btn-primary:disabled,
.btn-ghost:disabled {
  opacity: 0.55;
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
.link.danger {
  color: #e35151;
}
.link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@media (max-width: 1100px) {
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
  .right {
    text-align: left;
  }
  .actions {
    grid-column: 1 / -1;
    justify-content: flex-start;
    padding-top: var(--space-2);
    border-top: 1px dashed var(--border-subtle);
  }
  .manage-grid {
    grid-template-columns: 1fr;
  }
}
</style>
