<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { getMyAuthor, updateAuthor } from '@/api/authors'
import { uploadFile } from '@/api/files'
import { useAuthStore } from '@/stores/auth'
import { useSiteStore } from '@/stores/site'
import type { AuthorDto } from '@/types'

const auth = useAuthStore()
const site = useSiteStore()

const author = ref<AuthorDto | null>(null)
const loading = ref(true)
const saving = ref(false)
const uploading = ref(false)
const errorMsg = ref('')
const okMsg = ref('')
/** 账号未关联作者时的专门提示（不是错误，是待管理员处理的状态） */
const notLinked = ref(false)

const form = reactive({ name: '', email: '', bio: '', avatar: '' })
const avatarBroken = ref(false)

function fill(a: AuthorDto) {
  form.name = a.name
  form.email = a.email
  form.bio = a.bio
  form.avatar = a.avatar
  avatarBroken.value = false
}

async function load() {
  loading.value = true
  errorMsg.value = ''
  notLinked.value = false
  try {
    const a = await getMyAuthor()
    author.value = a
    fill(a)
  } catch (e) {
    // 后端在「账号未关联作者」时返回 404 并给出可操作提示
    notLinked.value = true
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)

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
    avatarBroken.value = false
    okMsg.value = '头像上传成功，记得保存'
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    uploading.value = false
    input.value = ''
  }
}

async function onSave() {
  if (!author.value) return
  if (!form.name.trim() || !form.email.trim()) {
    errorMsg.value = '姓名与邮箱不能为空'
    return
  }

  saving.value = true
  errorMsg.value = ''
  okMsg.value = ''
  try {
    const updated = await updateAuthor(author.value.id, {
      name: form.name.trim(),
      email: form.email.trim(),
      bio: form.bio,
      avatar: form.avatar,
      version: author.value.version,
    })
    author.value = updated
    fill(updated)
    okMsg.value = '已保存'
    // 文章详情与首屏站点信息都缓存了作者信息，保存后刷新
    await site.refreshAll()
    // 顺带刷新登录用户里的 authorName（顶栏显示用）
    await auth.restore()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    saving.value = false
  }
}

function onReset() {
  if (author.value) fill(author.value)
  errorMsg.value = ''
  okMsg.value = ''
}
</script>

<template>
  <section class="my-profile">
    <header class="page-head">
      <div>
        <h1>个人资料</h1>
        <p class="muted">这里的姓名与头像会显示在你发布的文章上</p>
      </div>
    </header>

    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <div v-if="loading" class="muted-block">加载中...</div>

    <!-- 账号未关联作者：给出明确指引，而不是让用户对着空表单猜 -->
    <div v-else-if="notLinked" class="card panel unlinked">
      <h3>账号未关联作者</h3>
      <p>
        你的登录账号（{{ auth.user?.email }}）还没有关联到任何作者。
        作者是文章的署名对象，需要由管理员在「账号管理 → 编辑 → 关联作者」中为你指定。
      </p>
      <p class="muted">在关联之前，你可以正常写文章，但文章不会显示你的署名信息。</p>
    </div>

    <form v-else-if="author" class="card panel" @submit.prevent="onSave">
      <div class="avatar-row">
        <div class="avatar-box">
          <img v-if="form.avatar && !avatarBroken" :src="form.avatar" alt="头像" @error="avatarBroken = true" />
          <span v-else class="avatar-fallback">{{ form.name.slice(0, 1) || '?' }}</span>
        </div>
        <div class="avatar-actions">
          <label class="btn-ghost">
            {{ uploading ? '上传中...' : '上传头像' }}
            <input type="file" accept="image/*" hidden :disabled="uploading" @change="onPickAvatar" />
          </label>
          <input v-model.trim="form.avatar" class="input" placeholder="或填写图片 URL" />
          <span class="hint">支持 jpg / png / webp / gif / svg，单文件不超过 10MB</span>
        </div>
      </div>

      <label class="field">
        <span class="label">姓名</span>
        <input v-model="form.name" class="input" maxlength="100" placeholder="展示在文章页的作者名" />
      </label>

      <label class="field">
        <span class="label">邮箱（仅展示/联系用）</span>
        <input v-model="form.email" class="input" maxlength="100" placeholder="author@example.com" />
      </label>

      <label class="field">
        <span class="label">个人简介</span>
        <textarea v-model="form.bio" class="input textarea" rows="4" maxlength="500" />
      </label>

      <footer class="form-actions">
        <span class="version">版本号 v{{ author.version }}</span>
        <div class="actions">
          <button type="button" class="btn-ghost" :disabled="saving" @click="onReset">重置</button>
          <button type="submit" class="btn-primary" :disabled="saving || uploading">
            {{ saving ? '保存中...' : '保存' }}
          </button>
        </div>
      </footer>
    </form>

    <div v-else class="muted-block">暂无作者资料</div>
  </section>
</template>

<style scoped>
.my-profile {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
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
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}

.panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  padding: var(--space-6);
  max-width: 720px;
}
.panel h3 {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}

.unlinked p {
  font: var(--text-body-sm);
  color: var(--text-default);
  line-height: 1.9;
  margin: 0;
}

.avatar-row {
  display: flex;
  align-items: center;
  gap: var(--space-5);
  flex-wrap: wrap;
}
.avatar-box {
  width: 88px;
  height: 88px;
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
  font-size: 32px;
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

.form-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  padding-top: var(--space-4);
  border-top: 1px solid var(--border-subtle);
  flex-wrap: wrap;
}
.version {
  font: var(--text-caption);
  color: var(--text-subtle);
}
.actions {
  display: flex;
  gap: var(--space-3);
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
</style>
