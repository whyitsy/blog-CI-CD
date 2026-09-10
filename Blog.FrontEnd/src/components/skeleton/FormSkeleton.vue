<script setup lang="ts">
/**
 * 表单骨架：站点配置 / 个人资料等「若干字段 + 保存按钮」的页面。
 */
withDefaults(defineProps<{ fields?: number }>(), { fields: 4 })
</script>

<template>
  <div class="form-skeleton card">
    <div class="sk-line shimmer sk-title w-40" />
    <div v-for="i in fields" :key="i" class="field">
      <div class="sk-line shimmer w-30" />
      <div class="sk-input shimmer" />
    </div>
    <div class="actions">
      <div class="sk-btn shimmer" />
    </div>
  </div>
</template>

<style scoped>
.form-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  padding: var(--space-6);
  max-width: 720px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.sk-input {
  height: 40px;
  border-radius: var(--radius-sm);
}

.actions {
  display: flex;
  justify-content: flex-end;
  padding-top: var(--space-4);
  border-top: 1px solid var(--border-subtle);
}

.sk-btn {
  width: 120px;
  height: 36px;
  border-radius: var(--radius-sm);
}
</style>
