<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { createCategory, getCategories } from '@/api/categories'
import { createTag, getTags } from '@/api/tags'
import type { CategoryDto, PostDetailDto, PostPayload, TagDto } from '@/types'

/** 编辑器 props：
 *  - initial: 编辑模式下传入的 PostDetailDto；
 *  - mode: 'create' 隐藏 version 并显示发布开关；'edit' 显示版本号并禁用 publish。
 */
const props = defineProps<{
  initial?: PostDetailDto | null
  mode: 'create' | 'edit'
  saving?: boolean
}>()

const emit = defineEmits<{
  (e: 'save', payload: PostPayload): void
  (e: 'cancel'): void
}>()

const title = ref('')
const summary = ref('')
const content = ref('')
const coverImage = ref('')
const categoryId = ref<string | null>(null)
const selectedTagIds = ref<string[]>([])
const publish = ref(true)

const categories = ref<CategoryDto[]>([])
const tags = ref<TagDto[]>([])

// 内联新建分类/标签
const newCategoryName = ref('')
const newTagName = ref('')
const inlineError = ref('')

const wordCount = computed(() => content.value.length)
const readMinutes = computed(() => Math.max(1, Math.round(wordCount.value / 400)))

function syncFromInitial() {
  if (!props.initial) {
    title.value = ''
    summary.value = ''
    content.value = ''
    coverImage.value = ''
    categoryId.value = null
    selectedTagIds.value = []
    publish.value = true
    return
  }
  title.value = props.initial.title
  summary.value = props.initial.summary
  content.value = props.initial.content
  coverImage.value = props.initial.coverImage
  categoryId.value = props.initial.categoryId
  selectedTagIds.value = props.initial.tags.map((t) => t.id)
}

watch(() => props.initial, syncFromInitial, { immediate: false })

async function loadTaxonomy() {
  const [cs, ts] = await Promise.all([getCategories(), getTags()])
  categories.value = cs
  tags.value = ts
}

onMounted(() => {
  syncFromInitial()
  loadTaxonomy().catch(() => {
    inlineError.value = '加载分类/标签失败'
  })
})

const titleError = computed(() => {
  const t = title.value.trim()
  if (!t) return '标题不能为空'
  if (t.length > 200) return '标题长度不能超过 200'
  return ''
})
const contentError = computed(() => (content.value.trim() ? '' : '内容不能为空'))
const formValid = computed(() => !titleError.value && !contentError.value)

async function addCategory() {
  const name = newCategoryName.value.trim()
  if (!name) return
  try {
    const created = await createCategory(name)
    categories.value = [...categories.value, created].sort((a, b) => a.name.localeCompare(b.name))
    categoryId.value = created.id
    newCategoryName.value = ''
  } catch (e) {
    inlineError.value = e instanceof Error ? e.message : '新建分类失败'
  }
}

async function addTag() {
  const name = newTagName.value.trim()
  if (!name) return
  if (tags.value.some((t) => t.name === name)) {
    inlineError.value = '标签已存在'
    return
  }
  try {
    const created = await createTag(name)
    tags.value = [...tags.value, created].sort((a, b) => a.name.localeCompare(b.name))
    selectedTagIds.value = [...selectedTagIds.value, created.id]
    newTagName.value = ''
  } catch (e) {
    inlineError.value = e instanceof Error ? e.message : '新建标签失败'
  }
}

function toggleTag(id: string) {
  if (selectedTagIds.value.includes(id)) {
    selectedTagIds.value = selectedTagIds.value.filter((t) => t !== id)
  } else {
    selectedTagIds.value = [...selectedTagIds.value, id]
  }
}

function submit() {
  if (!formValid.value) return
  const payload: PostPayload = {
    title: title.value.trim(),
    summary: summary.value.trim(),
    content: content.value,
    coverImage: coverImage.value.trim(),
    categoryId: categoryId.value,
    tagIds: [...selectedTagIds.value],
    publish: props.mode === 'create' ? publish.value : undefined,
    version: props.initial?.version,
  }
  emit('save', payload)
}
</script>

<template>
  <form class="post-editor card" @submit.prevent="submit">
    <header class="pe-head">
      <h2>{{ mode === 'create' ? '新建文章' : '编辑文章' }}</h2>
      <p v-if="mode === 'edit' && initial" class="pe-meta">
        版本 v{{ initial.version }} · 最近更新 {{ initial.updatedAt?.replace('T', ' ').slice(0, 16) }}
      </p>
    </header>

    <label class="field">
      <span class="field-label">标题 <em>*</em></span>
      <input v-model="title" type="text" maxlength="200" placeholder="给文章起一个清晰的标题" />
      <small v-if="titleError" class="err">{{ titleError }}</small>
      <small v-else class="hint">{{ title.length }} / 200</small>
    </label>

    <label class="field">
      <span class="field-label">摘要</span>
      <textarea v-model="summary" rows="2" placeholder="可选：留空将自动从前 100 字截取" />
    </label>

    <label class="field">
      <span class="field-label">封面 URL</span>
      <input v-model="coverImage" type="text" placeholder="/media/xxx.png 或 https://..." />
    </label>

    <div class="row">
      <label class="field">
        <span class="field-label">分类</span>
        <select v-model="categoryId">
          <option :value="null">未分类</option>
          <option v-for="c in categories" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </label>

      <div class="field">
        <span class="field-label">快速新建分类</span>
        <div class="inline-add">
          <input v-model="newCategoryName" type="text" placeholder="分类名" @keyup.enter="addCategory" />
          <button type="button" class="btn-mini" :disabled="!newCategoryName.trim()" @click="addCategory">添加</button>
        </div>
      </div>
    </div>

    <div class="field">
      <span class="field-label">标签（多选）</span>
      <div class="tag-pool">
        <button
          v-for="t in tags"
          :key="t.id"
          type="button"
          class="tag-chip"
          :class="{ active: selectedTagIds.includes(t.id) }"
          @click="toggleTag(t.id)"
        >
          {{ t.name }}
        </button>
        <span v-if="!tags.length" class="empty">还没有标签，下方新建</span>
      </div>
      <div class="inline-add">
        <input v-model="newTagName" type="text" placeholder="新标签名" @keyup.enter="addTag" />
        <button type="button" class="btn-mini" :disabled="!newTagName.trim()" @click="addTag">添加</button>
      </div>
    </div>

    <label class="field">
      <span class="field-label">正文 <em>*</em> · Markdown · 约 {{ readMinutes }} 分钟阅读</span>
      <textarea v-model="content" rows="14" placeholder="支持 Markdown：## 标题、- 列表、```代码块``` 等" />
      <small v-if="contentError" class="err">{{ contentError }}</small>
      <small v-else class="hint">{{ wordCount }} 字</small>
    </label>

    <label v-if="mode === 'create'" class="field switch-field">
      <input v-model="publish" type="checkbox" />
        <span>创建后立即发布（未勾选则为草稿）</span>
      </label>

    <p v-if="inlineError" class="inline-err">{{ inlineError }}</p>

    <footer class="pe-actions">
      <button type="button" class="btn-ghost" :disabled="saving" @click="emit('cancel')">取消</button>
      <button type="submit" class="btn-primary" :disabled="!formValid || saving">
        {{ saving ? '保存中...' : mode === 'create' ? '创建' : '保存修改' }}
      </button>
    </footer>
  </form>
</template>

<style scoped>
.post-editor {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-6);
  max-width: 880px;
  margin: 0 auto;
}

.pe-head h2 {
  font: var(--text-h2);
  color: var(--text-strong);
  margin: 0;
}
.pe-meta {
  font: var(--text-body-sm);
  color: var(--text-muted);
  margin-top: var(--space-1);
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.field-label {
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-default);
}
.field-label em {
  color: #e35151;
  font-style: normal;
}
.field input,
.field textarea,
.field select {
  width: 100%;
  padding: var(--space-2) var(--space-3);
  background: var(--bg-raised);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  color: var(--text-default);
  font: var(--text-body);
}
.field input:focus,
.field textarea:focus,
.field select:focus {
  outline: none;
  border-color: var(--brand-500);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--brand-500) 20%, transparent);
}
.field textarea {
  font-family: var(--font-mono);
  line-height: 1.6;
  resize: vertical;
}
.hint {
  font: var(--text-caption);
  color: var(--text-muted);
}
.err {
  font: var(--text-caption);
  color: #e35151;
}

.row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-4);
}
@media (max-width: 640px) {
  .row { grid-template-columns: 1fr; }
}

.inline-add {
  display: flex;
  gap: var(--space-2);
}
.btn-mini {
  padding: 0 var(--space-3);
  border-radius: var(--radius-sm);
  border: 1px solid var(--border-default);
  background: var(--bg-raised);
  color: var(--text-default);
  cursor: pointer;
}
.btn-mini:disabled { opacity: 0.5; cursor: not-allowed; }

.tag-pool {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  padding: var(--space-2);
  border: 1px dashed var(--border-subtle);
  border-radius: var(--radius-sm);
  min-height: 38px;
}
.tag-chip {
  padding: 2px var(--space-3);
  border: 1px solid var(--border-default);
  border-radius: 999px;
  background: var(--bg-raised);
  color: var(--text-default);
  font: var(--text-caption);
  cursor: pointer;
  transition: all var(--transition-fast);
}
.tag-chip:hover { border-color: var(--brand-500); }
.tag-chip.active {
  background: color-mix(in srgb, var(--brand-500) 15%, transparent);
  border-color: var(--brand-500);
  color: var(--brand-500);
}
.empty { color: var(--text-muted); font: var(--text-caption); padding: 4px; }

.switch-field {
  flex-direction: row;
  align-items: center;
  gap: var(--space-2);
}
.switch-field input { width: auto; }

.pe-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-3);
  padding-top: var(--space-3);
  border-top: 1px solid var(--border-subtle);
}
.btn-primary {
  padding: var(--space-2) var(--space-5);
  border: none;
  border-radius: var(--radius-sm);
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
  color: #fff;
  font-weight: 600;
  cursor: pointer;
}
.btn-primary:disabled { opacity: 0.6; cursor: not-allowed; }
.btn-ghost {
  padding: var(--space-2) var(--space-5);
  border-radius: var(--radius-sm);
  border: 1px solid var(--border-default);
  background: transparent;
  color: var(--text-default);
  cursor: pointer;
}
.btn-ghost:disabled { opacity: 0.5; cursor: not-allowed; }

.inline-err {
  color: #e35151;
  font: var(--text-caption);
  background: color-mix(in srgb, #e35151 8%, transparent);
  padding: var(--space-2);
  border-radius: var(--radius-sm);
}
</style>