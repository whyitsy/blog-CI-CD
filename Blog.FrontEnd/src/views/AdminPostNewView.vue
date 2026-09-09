<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import PostEditor from '@/components/admin/PostEditor.vue'
import { createPost } from '@/api/posts'
import type { PostDetailDto, PostPayload } from '@/types'

const router = useRouter()
const saving = ref(false)
const errorMsg = ref('')

async function onSave(payload: PostPayload) {
  saving.value = true
  errorMsg.value = ''
  try {
    const created: PostDetailDto = await createPost(payload)
    router.push(`/post/${created.id}`)
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '创建失败'
  } finally {
    saving.value = false
  }
}

function onCancel() {
  router.push('/admin')
}
</script>

<template>
  <section class="admin-new container">
    <header class="page-header">
      <button class="link" @click="router.push('/admin')">← 返回列表</button>
    </header>
    <p v-if="errorMsg" class="err-banner">{{ errorMsg }}</p>
    <PostEditor mode="create" :saving="saving" @save="onSave" @cancel="onCancel" />
  </section>
</template>

<style scoped>
.admin-new {
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
</style>