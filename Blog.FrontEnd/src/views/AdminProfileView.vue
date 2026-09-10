<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { getAuthors, updateAuthor } from '@/api/authors'
import { uploadFile } from '@/api/files'
import { useSiteStore } from '@/stores/site'
import type { AuthorDto } from '@/types'

const site = useSiteStore()

const author = ref<AuthorDto | null>(null)
const loading = ref(true)
const saving = ref(false)
const uploading = ref(false)
const errorMsg = ref('')
const okMsg = ref('')

const form = reactive({
  name: '',
  email: '',
  avatar: '',
  bio: '',
})

/** 头像加载失败时退回首字母占位（历史数据里的 /media/xxx 已不再对外提供） */
const avatarBroken = ref(false)

const avatarPreview = computed(() => (!avatarBroken.value && form.avatar ? form.avatar : ''))

function fill(a: AuthorDto) {
  form.name = a.name
  form.email = a.email
  form.avatar = a.avatar
  form.bio = a.bio
  avatarBroken.value = false
}

/** 换上新地址后重新尝试加载 */
watch(
  () => form.avatar,
  () => {
    avatarBroken.value = false
  },
)

async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    const authors = await getAuthors()
    if (!authors.length) {
      errorMsg.value = '后端还没有作者记录，请先在数据库中初始化博主资料'
      return
    }
    // 当前无认证，单人博客：取首条作为博主
    author.value = authors[0]
    fill(authors[0])
  } catch (e) {
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
    okMsg.value = '头像上传成功，记得点击保存'
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    uploading.value = false
    input.value = ''
  }
}

async function onSave() {
  if (!author.value) return
  if (!form.name.trim()) {
    errorMsg.value = '名称不能为空'
    return
  }
  if (!form.email.trim()) {
    errorMsg.value = '邮箱不能为空'
    return
  }

  saving.value = true
  errorMsg.value = ''
  okMsg.value = ''
  try {
    const updated = await updateAuthor(author.value.id, {
      name: form.name.trim(),
      email: form.email.trim(),
      avatar: form.avatar,
      bio: form.bio,
      version: author.value.version,
    })
    author.value = updated
    fill(updated)
    okMsg.value = '已保存'
    // NavBar / Footer / 详情页作者信息来自 site store，保存后重新拉取
    await site.refreshAll()
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
  <section class="admin-profile">
    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <div v-if="loading" class="muted-block">加载中...</div>

    <form v-else-if="author" class="profile-form card" @submit.prevent="onSave">
      <div class="avatar-row">
        <div class="avatar-box">
          <img v-if="avatarPreview" :src="avatarPreview" alt="头像" @error="avatarBroken = true" />
          <span v-else class="avatar-fallback">{{ form.name.slice(0, 1) || '?' }}</span>
        </div>
        <div class="avatar-actions">
          <label class="btn-ghost">
            {{ uploading ? '上传中...' : '上传头像' }}
            <input type="file" accept="image/*" hidden :disabled="uploading" @change="onPickAvatar" />
          </label>
          <input v-model.trim="form.avatar" class="input" placeholder="或直接填写图片 URL" />
          <span class="hint">支持 jpg / png / webp / gif / svg，单文件不超过 10MB</span>
        </div>
      </div>

      <label class="field">
        <span class="label">名称</span>
        <input v-model="form.name" class="input" maxlength="50" placeholder="展示在文章详情页的作者名" />
      </label>

      <label class="field">
        <span class="label">邮箱</span>
        <input v-model="form.email" class="input" type="email" maxlength="100" placeholder="用于 Gravatar 或联系展示" />
      </label>

      <label class="field">
        <span class="label">个人简介</span>
        <textarea v-model="form.bio" class="input textarea" rows="4" maxlength="500" placeholder="一句话介绍自己" />
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

    <div v-else class="muted-block">暂无博主资料</div>
  </section>
</template>

<style scoped>
.admin-profile {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
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

.profile-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  padding: var(--space-6);
  max-width: 720px;
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

.input {
  width: 100%;
  padding: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-strong);
  background: var(--bg-canvas);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  outline: none;
  transition: border-color var(--transition-fast);
}
.input:focus {
  border-color: var(--brand-500);
}
.textarea {
  resize: vertical;
  font-family: inherit;
}

.hint {
  font: var(--text-caption);
  color: var(--text-subtle);
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

.btn-primary,
.btn-ghost {
  padding: var(--space-2) var(--space-4);
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: opacity var(--transition-fast), border-color var(--transition-fast);
}
.btn-primary {
  border: none;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}
.btn-ghost {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--border-default);
  background: transparent;
  color: var(--text-default);
}
.btn-ghost:hover {
  border-color: var(--border-strong);
}
.btn-primary:disabled,
.btn-ghost:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
</style>
