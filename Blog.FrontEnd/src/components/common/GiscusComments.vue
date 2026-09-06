<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

const container = ref<HTMLElement>()

/** giscus 评论（GitHub Discussions 驱动），按当前主题适配 */
function loadGiscus() {
  if (!container.value) return
  container.value.innerHTML = ''

  const theme = document.documentElement.dataset.theme === 'light' ? 'light' : 'dark'
  const script = document.createElement('script')
  script.src = 'https://giscus.app/client.js'
  script.async = true
  script.crossOrigin = 'anonymous'
  const attrs: Record<string, string> = {
    'data-repo': 'whyitsy/blog-comment',
    'data-repo-id': 'R_kgDOUODt5w',
    'data-category': 'Announcements',
    'data-category-id': 'DIC_kwDOUODt584DE2uV',
    'data-mapping': 'pathname',
    'data-strict': '0',
    'data-reactions-enabled': '1',
    'data-emit-metadata': '0',
    'data-input-position': 'top',
    'data-theme': theme,
    'data-lang': 'zh-CN',
    'data-loading': 'lazy',
  }
  for (const [k, v] of Object.entries(attrs)) script.setAttribute(k, v)
  container.value.appendChild(script)
}

// 监听主题切换重载评论
const observer = new MutationObserver(() => loadGiscus())

onMounted(() => {
  loadGiscus()
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
})

onBeforeUnmount(() => observer.disconnect())
</script>

<template>
  <div ref="container" class="giscus-container" />
</template>

<style scoped>
.giscus-container {
  margin-top: var(--space-8);
  min-height: 120px;
}
</style>
