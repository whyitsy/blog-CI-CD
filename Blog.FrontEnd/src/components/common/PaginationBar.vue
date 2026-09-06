<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  page: number
  totalPages: number
  totalCount?: number
}>()

const emit = defineEmits<{ (e: 'change', page: number): void }>()

/** 页码序列：1 … 当前±2 … 末页 */
const pages = computed<(number | '...')[]>(() => {
  const total = props.totalPages
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)

  const current = props.page
  const set = new Set<number>([1, total, current - 1, current, current + 1])
  if (current <= 3) {
    set.add(2)
    set.add(3)
    set.add(4)
  }
  if (current >= total - 2) {
    set.add(total - 1)
    set.add(total - 2)
    set.add(total - 3)
  }

  const sorted = [...set].filter((p) => p >= 1 && p <= total).sort((a, b) => a - b)
  const result: (number | '...')[] = []
  let prev = 0
  for (const p of sorted) {
    if (prev && p - prev > 1) result.push('...')
    result.push(p)
    prev = p
  }
  return result
})

function go(p: number) {
  if (p >= 1 && p <= props.totalPages && p !== props.page) emit('change', p)
}
</script>

<template>
  <nav v-if="totalPages > 1" class="pagination" aria-label="分页">
    <button class="page-btn" :disabled="page <= 1" @click="go(page - 1)">上一页</button>

    <template v-for="(p, i) in pages" :key="i">
      <span v-if="p === '...'" class="page-ellipsis">…</span>
      <button v-else class="page-btn page-num" :class="{ active: p === page }" @click="go(p)">
        {{ p }}
      </button>
    </template>

    <button class="page-btn" :disabled="page >= totalPages" @click="go(page + 1)">下一页</button>
  </nav>
</template>

<style scoped>
.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  margin-top: var(--space-10);
  flex-wrap: wrap;
}

.page-btn {
  min-width: 38px;
  height: 38px;
  padding: 0 var(--space-3);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  font-weight: 500;
  color: var(--text-muted);
  background: var(--bg-surface);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  transition: all var(--transition-fast);
}

.page-btn:hover:not(:disabled):not(.active) {
  color: var(--text-strong);
  border-color: var(--border-default);
  transform: translateY(-1px);
}

.page-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.page-btn.active {
  color: #fff;
  border: none;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
  box-shadow: var(--glow-purple);
}

.page-ellipsis {
  color: var(--text-subtle);
  padding: 0 var(--space-1);
}
</style>
