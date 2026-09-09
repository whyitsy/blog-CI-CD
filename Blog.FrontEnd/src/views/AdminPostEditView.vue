<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PostEditor from '@/components/admin/PostEditor.vue'
import { getPostDetail, updatePost } from '@/api/posts'
import type { PostDetailDto, PostPayload } from '@/types'

const route = useRoute()
const router = useRouter()

const post = ref<PostDetailDto | null>(null)
const loading = ref(true)
const saving = ref(false)
const errorMsg = ref('')

async function load(id: string) {
  loading.value = true
  errorMsg.value = ''
  post.value = null
  try {
    post.value = await getPostDetail(id)
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

watch(
  () => route.params.id,
  (id) => id && load(String(id)),
  { immediate: true },
)

onMounted(() => {
  if (route.params.id) load(String(route.params.id))
})

async function onSave(payload: PostPayload) {
  if (!post.value) return
  saving.value = true
  errorMsg.value = ''
  try {
    const updated = await updatePost(post.value.id, payload)
    post.value = updated
    router.push(`/post/${updated.id}`)
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    saving.value = false
  }
}

function onCancel() {
  router.push('/admin')
}
</script>

<template>
  <section class="admin-edit container">
    <header class="page-header">
      <button class="link" @click="router.push('/admin')">← 返回列表</button>
    </header>
    <p v-if="errorMsg" class="err-banner">{{ errorMsg }}</p>
    <div v-if="loading" class="loading">加载中...</div>
    <PostEditor
      v-else-if="post"
      mode="edit"
      :initial="post"
      :saving="saving"
      @save="onSave"
      @cancel="onCancel"
    />
  </section>
</template>

<style scoped>
.admin-edit {
  padding: var(--space-8) var(--space-4) var(--space-16);
}
.page-header {
  max-width: 880px;
  margin: 0 auto var(--space-3);
}
.link {
  background: none;
  border: none;
  padding: 0;
  color: var(--brand-500);
  cursor: pointer;
  font: var(--text-body-sm);
}
.err-banner {
  max-width: 880px;
  margin: 0 auto var(--space-4);
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
  padding: var(--space-3);
  border-radius: var(--radius-sm);
}
.loading {
  text-align: center;
  padding: var(--space-12);
  color: var(--text-muted);
}
</style>