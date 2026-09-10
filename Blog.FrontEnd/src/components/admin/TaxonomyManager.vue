<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { CategoryDto, TagDto } from '@/types'

/** 分类与标签的字段/接口完全一致，只有文案与端点不同，因此共用一套管理 UI */
type TaxonomyKind = 'category' | 'tag'
type TaxonomyItem = CategoryDto | TagDto

const props = defineProps<{
  kind: TaxonomyKind
  /** 是否展示「文章数」列 */
  showPostCount?: boolean
  load: () => Promise<TaxonomyItem[]>
  create: (name: string) => Promise<TaxonomyItem>
  update: (id: string, name: string, version: number) => Promise<TaxonomyItem>
  remove: (id: string, version: number) => Promise<null>
}>()

const items = ref<TaxonomyItem[]>([])
const loading = ref(false)
const busy = ref(false)
const errorMsg = ref('')
const okMsg = ref('')

const newName = ref('')
const editingId = ref<string | null>(null)
const editingName = ref('')

function label(_singular = false) {
  return props.kind === 'category' ? '分类' : '标签'
}

async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    items.value = await props.load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function onCreate() {
  const name = newName.value.trim()
  if (!name) {
    errorMsg.value = `请填写${label(true)}名称`
    return
  }

  busy.value = true
  errorMsg.value = ''
  okMsg.value = ''
  try {
    await props.create(name)
    newName.value = ''
    okMsg.value = '已新增'
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '新增失败'
  } finally {
    busy.value = false
  }
}

function startEdit(item: TaxonomyItem) {
  editingId.value = item.id
  editingName.value = item.name
  errorMsg.value = ''
  okMsg.value = ''
}

function cancelEdit() {
  editingId.value = null
  editingName.value = ''
}

async function saveEdit(item: TaxonomyItem) {
  const name = editingName.value.trim()
  if (!name) {
    errorMsg.value = `请填写${label(true)}名称`
    return
  }
  if (name === item.name) {
    cancelEdit()
    return
  }

  busy.value = true
  errorMsg.value = ''
  okMsg.value = ''
  try {
    await props.update(item.id, name, item.version)
    okMsg.value = '已保存'
    cancelEdit()
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    busy.value = false
  }
}

async function onDelete(item: TaxonomyItem) {
  const extra =
    props.kind === 'category'
      ? '该分类下文章的所属分类将被置空。'
      : '文章与该标签的关联会一并失效。'
  if (!window.confirm(`确认删除「${item.name}」？${extra}`)) return

  busy.value = true
  errorMsg.value = ''
  okMsg.value = ''
  try {
    await props.remove(item.id, item.version)
    okMsg.value = '已删除'
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '删除失败'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="taxonomy">
    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <form class="add-bar card" @submit.prevent="onCreate">
      <input
        v-model="newName"
        class="input"
        maxlength="100"
        :placeholder="`新增${label(true)}，例如「${kind === 'category' ? '后端' : 'Vue'}」`"
      />
      <button type="submit" class="btn-primary" :disabled="busy || !newName.trim()">新增</button>
    </form>

    <div v-if="loading" class="muted-block">加载中...</div>

    <div v-else-if="items.length" class="list card">
      <div class="list-head">
        <span>名称</span>
        <span v-if="showPostCount">文章数</span>
        <span>版本</span>
        <span class="right">操作</span>
      </div>

      <div v-for="item in items" :key="item.id" class="list-row">
        <template v-if="editingId === item.id">
          <input
            v-model="editingName"
            class="input"
            maxlength="100"
            @keyup.enter="saveEdit(item)"
            @keyup.esc="cancelEdit"
          />
          <span v-if="showPostCount" class="muted">{{ item.postCount }}</span>
          <span class="muted">v{{ item.version }}</span>
          <span class="right actions">
            <button class="link" :disabled="busy" @click="saveEdit(item)">保存</button>
            <button class="link muted-link" :disabled="busy" @click="cancelEdit">取消</button>
          </span>
        </template>

        <template v-else>
          <span class="name">{{ item.name }}</span>
          <span v-if="showPostCount" class="muted">{{ item.postCount }} 篇</span>
          <span class="muted">v{{ item.version }}</span>
          <span class="right actions">
            <button class="link" :disabled="busy" @click="startEdit(item)">编辑</button>
            <button class="link danger" :disabled="busy" @click="onDelete(item)">删除</button>
          </span>
        </template>
      </div>
    </div>

    <div v-else class="muted-block">还没有任何{{ label(false) }}</div>
  </section>
</template>

<style scoped>
.taxonomy {
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

.add-bar {
  display: flex;
  gap: var(--space-3);
  padding: var(--space-4);
  max-width: 560px;
}

/* 按钮不要被 flex 压缩变窄 */
.add-bar .btn-primary {
  flex: 0 0 auto;
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

.muted-block {
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}

.list {
  overflow: hidden;
  max-width: 760px;
}

.list-head,
.list-row {
  display: grid;
  grid-template-columns: 1fr 96px 72px 128px;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-3) var(--space-4);
}
.list-head {
  background: var(--bg-raised);
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}
.list-row {
  border-top: 1px solid var(--border-subtle);
}
.list-row:first-child {
  border-top: none;
}

.name {
  color: var(--text-strong);
  font-weight: 600;
}
.muted {
  font: var(--text-caption);
  color: var(--text-subtle);
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
  flex-shrink: 0;
  padding: var(--space-2) var(--space-4);
  border: none;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  color: #fff;
  cursor: pointer;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}
.btn-primary:disabled {
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
}
.link.muted-link {
  color: var(--text-subtle);
}
.link.danger {
  color: #e35151;
}
.link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@media (max-width: 640px) {
  .list-head {
    display: none;
  }
  .list-row {
    grid-template-columns: 1fr;
    gap: var(--space-2);
  }
  .right {
    text-align: left;
  }
  .actions {
    justify-content: flex-start;
  }
}
</style>
