<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import PostEditor from '@/components/admin/PostEditor.vue'
import { createPost } from '@/api/posts'
import type { PostPayload } from '@/types'

const router = useRouter()
const saving = ref(false)
const errorMsg = ref('')

async function onSave(payload: PostPayload) {
  saving.value = true
  errorMsg.value = ''
  try {
    await createPost(payload)
    router.push('/admin')
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
  <section class="admin-new">
    <p v-if="errorMsg" class="err-banner">{{ errorMsg }}</p>
    <PostEditor mode="create" :saving="saving" @save="onSave" @cancel="onCancel" />
  </section>
</template>

<style scoped>
.admin-new {
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
</style>
