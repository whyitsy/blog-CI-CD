<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PostEditor from '@/components/admin/PostEditor.vue'
import { getPostDetailReadonly, updatePost } from '@/api/posts'
import type { PostDetailDto, PostPayload } from '@/types'

const route = useRoute()
const router = useRouter()

const post = ref<PostDetailDto | null>(null)
const loading = ref(true)
const saving = ref(false)
const errorMsg = ref('')

const postId = computed(() => String(route.params.id ?? ''))

async function load(id: string) {
  loading.value = true
  errorMsg.value = ''
  post.value = null
  try {
    // 只读端点不污染浏览量；且后端会做归属校验，非本人文章返回 404
    post.value = await getPostDetailReadonly(id)
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

watch(postId, (id) => { if (id) load(id) }, { immediate: true })

async function onSave(payload: PostPayload) {
  if (!post.value) return
  saving.value = true
  errorMsg.value = ''
  try {
    await updatePost(post.value.id, payload)
    router.push({ name: 'my-posts' })
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    saving.value = false
  }
}

function onCancel() {
  router.push({ name: 'my-posts' })
}
</script>

<template>
  <section class="my-post-edit">
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
.my-post-edit {
  max-width: 880px;
}
.err-banner {
  margin-bottom: var(--space-4);
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-sm);
  font: var(--text-body-sm);
}
.loading {
  text-align: center;
  padding: var(--space-12);
  color: var(--text-muted);
}
</style>
