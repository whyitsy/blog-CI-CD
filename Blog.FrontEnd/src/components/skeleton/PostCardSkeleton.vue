<script setup lang="ts">
/**
 * 文章卡片骨架（首页 / 列表页）。
 * 尺寸与 PostCard 对齐，避免加载完成后的布局跳动。
 */
withDefaults(defineProps<{ count?: number }>(), { count: 12 })
</script>

<template>
  <div class="grid">
    <div v-for="i in count" :key="i" class="card sk-card">
      <div class="sk-cover shimmer" />
      <div class="sk-body">
        <div class="sk-line shimmer w-70" />
        <div class="sk-line shimmer w-100" />
        <div class="sk-line shimmer w-40" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-6);
}

.sk-card {
  overflow: hidden;
}

.sk-cover {
  aspect-ratio: 16 / 9;
  width: 100%;
}

.sk-body {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-4);
}

@media (max-width: 1199px) {
  .grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 767px) {
  .grid {
    grid-template-columns: 1fr;
  }
}
</style>
