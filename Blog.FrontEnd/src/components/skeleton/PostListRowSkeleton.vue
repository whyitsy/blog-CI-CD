<script setup lang="ts">
/**
 * 表格行骨架（管理端文章列表 / 我的文章）。
 * 列数与 AdminPostListView 的 grid 一致，避免加载后横向跳动。
 */
withDefaults(
  defineProps<{
    rows?: number
    /** 列数：管理端文章表为 6 列 */
    columns?: number
  }>(),
  { rows: 6, columns: 6 },
)
</script>

<template>
  <div class="table">
    <div class="row head-row">
      <span v-for="c in columns" :key="c" class="sk-line shimmer" />
    </div>
    <div v-for="r in rows" :key="r" class="row">
      <span v-for="c in columns" :key="c" class="sk-line shimmer" :class="c === 1 ? 'w-80' : 'w-50'" />
    </div>
  </div>
</template>

<style scoped>
.table {
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.row {
  display: grid;
  grid-template-columns: minmax(0, 2fr) 1fr minmax(0, 1.4fr) 76px 132px 148px;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}
.row:last-child {
  border-bottom: none;
}

.head-row {
  background: var(--bg-raised);
}

@media (max-width: 880px) {
  .head-row {
    display: none;
  }
  .row {
    grid-template-columns: 1fr;
    gap: var(--space-2);
  }
}
</style>
