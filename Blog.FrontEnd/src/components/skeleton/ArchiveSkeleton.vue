<script setup lang="ts">
/**
 * 归档时间轴骨架：年节点 + 若干文章行。
 * 与 ArchiveView 的时间轴结构对应。
 */
withDefaults(defineProps<{ groups?: number }>(), { groups: 3 })
</script>

<template>
  <div class="archive-skeleton">
    <section v-for="g in groups" :key="g" class="year-block">
      <div class="year-node">
        <span class="year-dot shimmer" />
        <span class="sk-line shimmer w-30" />
      </div>
      <div class="sk-stack items">
        <div v-for="i in 3" :key="i" class="item">
          <span class="sk-line shimmer w-60" />
          <span class="sk-line shimmer w-30" />
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.archive-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
}

.year-block {
  display: grid;
  grid-template-columns: 140px minmax(0, 1fr);
  gap: var(--space-6);
}

.year-node {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}

.year-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  flex-shrink: 0;
}

.items {
  gap: var(--space-4);
}

.item {
  display: flex;
  align-items: center;
  gap: var(--space-4);
}

@media (max-width: 767px) {
  .year-block {
    grid-template-columns: 1fr;
    gap: var(--space-3);
  }
}
</style>
