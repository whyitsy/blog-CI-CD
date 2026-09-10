<script setup lang="ts">
/**
 * 按钮墙骨架：标签墙 / 分类墙 / 专栏墙。
 * 与三者的 .wall-btn 网格对应。
 */
withDefaults(defineProps<{ count?: number }>(), { count: 10 })
</script>

<template>
  <div class="wall">
    <div v-for="i in count" :key="i" class="wall-skeleton shimmer" />
  </div>
</template>

<style scoped>
.wall {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  justify-content: center;
}

.wall-skeleton {
  width: 132px;
  height: 52px;
  border-radius: var(--radius-lg);
}
</style>
