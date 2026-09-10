<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import {
  createAuthor,
  deleteAuthor,
  getAuthors,
  updateAuthor,
  type CreateAuthorPayload,
} from '@/api/authors'
import { uploadFile } from '@/api/files'
import { useSiteStore } from '@/stores/site'
import type { AuthorDto } from '@/types'

const site = useSiteStore()

const items = ref<AuthorDto[]>([])
const loading = ref(false)
const busy = ref(false)
const errorMsg = ref('')
const okMsg = ref('')

const showForm = ref(false)
const editingId = ref<string | null>(null)
const uploading = ref(false)

const form = reactive<CreateAuthorPayload>({
  name: '',
  email: '',
  bio: '',
  avatar: '',
})

async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    items.value = await getAuthors()
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
  form.name = ''
  form.email = ''
  form.bio = ''
  form.avatar = ''
}

function openCreate() {
  resetForm()
  showForm.value = true
}

function openEdit(a: AuthorDto) {
  editingId.value = a.id
  form.name = a.name
  form.email = a.email
  form.bio = a.bio
  form.avatar = a.avatar
  showForm.value = true
}

async function onPickAvatar(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  if (!file.type.startsWith('image/')) {
    errorMsg.value = '请选择图片文件'
    input.value = ''
    return
  }
  uploading.value = true
  errorMsg.value = ''
  try {
    form.avatar = await uploadFile(file)
    flash('头像上传成功，记得保存')
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    uploading.value = false
    input.value = ''
  }
}

async function onSubmit() {
  errorMsg.value = ''
  if (!form.name.trim() || !form.email.trim()) {
    errorMsg.value = '姓名与邮箱不能为空'
    return
  }

  busy.value = true
  try {
    if (editingId.value) {
      const target = items.value.find((a) => a.id === editingId.value)
      await updateAuthor(editingId.value, {
        name: form.name.trim(),
        email: form.email.trim(),
        bio: form.bio,
        avatar: form.avatar,
        version: target?.version ?? 1,
      })
      flash('已保存')
    } else {
      await createAuthor({
        name: form.name.trim(),
        email: form.email.trim(),
        bio: form.bio,
        avatar: form.avatar,
      })
      flash('作者已创建')
    }
    showForm.value = false
    resetForm()
    await load()
    // NavBar / Footer 的站点信息与文章详情的作者信息都来自缓存
    await site.refreshAll()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    busy.value = false
  }
}

async function onDelete(a: AuthorDto) {
  if (
    !window.confirm(
      `确认删除作者「${a.name}」？\n这是软删除：其署名文章的作者会被置空，文章本身不会被删除。`,
    )
  )
    return

  busy.value = true
  errorMsg.value = ''
  try {
    await deleteAuthor(a.id, a.version)
    flash('作者已删除')
    await load()
    await site.refreshAll()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '删除失败'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="admin-authors">
    <div class="toolbar">
      <span class="toolbar-info">共 {{ items.length }} 位作者</span>
      <button class="btn-primary" @click="showForm ? ((showForm = false), resetForm()) : openCreate()">
        {{ showForm ? '收起' : '+ 新建作者' }}
      </button>
    </div>

    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <!-- 新建 / 编辑 -->
    <form v-if="showForm" class="card panel" @submit.prevent="onSubmit">
      <h3>{{ editingId ? '编辑作者' : '新建作者' }}</h3>
      <p class="panel-hint">
        作者是**文章的署名对象**（内容层），不是登录账号。给作者开通登录请到「账号管理」。
      </p>

      <div class="avatar-row">
        <div class="avatar-box">
          <img v-if="form.avatar" :src="form.avatar" alt="头像" />
          <span v-else class="avatar-fallback">{{ form.name.slice(0, 1) || '?' }}</span>
        </div>
        <div class="avatar-actions">
          <label class="btn-ghost">
            {{ uploading ? '上传中...' : '上传头像' }}
            <input type="file" accept="image/*" hidden :disabled="uploading" @change="onPickAvatar" />
          </label>
          <input v-model.trim="form.avatar" class="input" placeholder="或填写图片 URL" />
        </div>
      </div>

      <div class="grid">
        <label class="field">
          <span class="label">姓名</span>
          <input v-model="form.name" class="input" maxlength="100" placeholder="展示在文章页的作者名" />
        </label>
        <label class="field">
          <span class="label">邮箱（仅展示/联系用）</span>
          <input v-model="form.email" class="input" maxlength="100" placeholder="author@example.com" />
        </label>
      </div>

      <label class="field">
        <span class="label">个人简介</span>
        <textarea v-model="form.bio" class="input textarea" rows="3" maxlength="500" />
      </label>

      <footer class="panel-actions">
        <button type="button" class="btn-ghost" @click="((showForm = false), resetForm())">取消</button>
        <button type="submit" class="btn-primary" :disabled="busy || uploading">
          {{ busy ? '保存中...' : editingId ? '保存' : '创建' }}
        </button>
      </footer>
    </form>

    <div v-if="loading" class="muted-block">加载中...</div>

    <div v-else class="list card">
      <div class="row head-row">
        <span>作者</span>
        <span>邮箱</span>
        <span>简介</span>
        <span>版本</span>
        <span class="right">操作</span>
      </div>

      <div v-for="a in items" :key="a.id" class="row">
        <span class="col-author">
          <span class="mini-avatar">
            <img v-if="a.avatar" :src="a.avatar" :alt="a.name" />
            <span v-else>{{ a.name.slice(0, 1) }}</span>
          </span>
          <span class="name">{{ a.name }}</span>
        </span>
        <span class="muted ellipsis">{{ a.email }}</span>
        <span class="muted ellipsis">{{ a.bio || '—' }}</span>
        <span class="muted">v{{ a.version }}</span>
        <span class="right actions">
          <button class="link" :disabled="busy" @click="openEdit(a)">编辑</button>
          <button class="link danger" :disabled="busy" @click="onDelete(a)">删除</button>
        </span>
      </div>

      <p v-if="!items.length" class="empty-row">还没有作者</p>
    </div>

    <p class="foot-hint">
      说明：本页维护的是**署名信息**。登录账号（邮箱/密码/角色）在「账号管理」里维护，
      并通过「关联作者」把某个账号绑定到这里的作者。
    </p>
  </section>
</template>

<style scoped>
.admin-authors {
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
.panel-hint {
  font: var(--text-caption);
  color: var(--text-subtle);
  margin: 0;
}

.avatar-row {
  display: flex;
  align-items: center;
  gap: var(--space-5);
  flex-wrap: wrap;
}
.avatar-box {
  width: 72px;
  height: 72px;
  flex-shrink: 0;
  border-radius: 50%;
  overflow: hidden;
  display: grid;
  place-items: center;
  border: 1px solid var(--border-default);
  background: var(--bg-raised);
}
.avatar-box img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.avatar-fallback {
  font-size: 26px;
  font-weight: 700;
  color: var(--brand-500);
}
.avatar-actions {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  flex: 1;
  min-width: 220px;
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

.panel-actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
}

.muted-block {
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}

.list {
  overflow: hidden;
}
.row {
  display: grid;
  grid-template-columns: minmax(0, 1.1fr) minmax(0, 1.2fr) minmax(0, 1.6fr) 60px 140px;
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

.col-author {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 0;
}
.mini-avatar {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  flex-shrink: 0;
  border-radius: 50%;
  overflow: hidden;
  font-size: 13px;
  font-weight: 700;
  color: var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 12%, transparent);
}
.mini-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.name {
  color: var(--text-strong);
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.muted {
  font: var(--text-caption);
  color: var(--text-subtle);
}
.ellipsis {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.right {
  text-align: right;
}
.actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
}

.empty-row {
  padding: var(--space-8);
  text-align: center;
  color: var(--text-subtle);
  font: var(--text-body-sm);
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
  display: inline-flex;
  align-items: center;
  justify-content: center;
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

.foot-hint {
  margin: 0;
  font: var(--text-caption);
  color: var(--text-subtle);
  line-height: 1.8;
}

@media (max-width: 1100px) {
  .head-row {
    display: none;
  }
  .row {
    grid-template-columns: 1fr auto;
    gap: var(--space-2) var(--space-3);
  }
  .col-author {
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
}
</style>
