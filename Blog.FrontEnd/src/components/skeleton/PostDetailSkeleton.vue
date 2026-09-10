<script setup lang="ts">
/**
 * 文章详情骨架：标题 + 元信息 + 正文段落 + 右侧目录占位。
 * 与 PostDetailView 的布局对应（窄屏隐藏侧栏）。
 */
</script>

<template>
  <div class="detail-skeleton container">
    <div class="main">
      <div class="sk-line shimmer sk-title w-80" />
      <div class="meta">
        <div class="sk-line shimmer w-30" />
        <div class="sk-line shimmer w-30" />
      </div>
      <div class="cover shimmer" />
      <div class="sk-stack">
        <div v-for="i in 3" :key="`p1-${i}`" class="sk-line shimmer w-100" />
        <div class="sk-line shimmer w-60" />
        <div v-for="i in 4" :key="`p2-${i}`" class="sk-line shimmer w-100" />
        <div class="sk-line shimmer w-40" />
      </div>
    </div>

    <aside class="side">
      <div class="sk-line shimmer w-60" />
      <div class="sk-stack">
        <div v-for="i in 5" :key="`t-${i}`" class="sk-line shimmer" :class="i % 2 ? 'w-80' : 'w-60'" />
      </div>
    </aside>
  </div>
</template>

<style scoped>
.detail-skeleton {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 240px;
  gap: var(--space-10);
  padding-top: 96px;
  padding-bottom: var(--space-16);
}

.main {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.meta {
  display: flex;
  gap: var(--space-4);
}

.cover {
  aspect-ratio: 16 / 9;
  width: 100%;
  border-radius: var(--radius-lg);
}

.side {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding-top: var(--space-4);
}

@media (max-width: 1023px) {
  .detail-skeleton {
    grid-template-columns: 1fr;
  }
  .side {
    display: none;
  }
}
</style>
