<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

// 不存在的路径在地址栏里也展示出来，便于排查（此前是静默重定向首页）
const attempted = route.fullPath
</script>

<template>
  <section class="not-found container">
    <div class="aurora-blobs" />
    <p class="code gradient-text">404</p>
    <h1>页面不存在</h1>
    <p class="desc">
      找不到 <code>{{ attempted }}</code>
      <br />
      它可能已被删除，或者链接拼写有误。
    </p>
    <div class="actions">
      <button class="btn-primary" @click="router.push('/')">返回首页</button>
      <button class="btn-ghost" @click="router.back()">返回上一页</button>
    </div>
  </section>
</template>

<style scoped>
.not-found {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--space-3);
  min-height: 70vh;
  text-align: center;
  padding-top: 64px;
  overflow: hidden;
}

.code {
  font-size: 96px;
  font-weight: 700;
  line-height: 1;
  margin: 0;
}

h1 {
  font: var(--text-h2);
  color: var(--text-strong);
  margin: 0;
}

.desc {
  font: var(--text-body-sm);
  color: var(--text-muted);
  line-height: 1.9;
}

code {
  font-family: var(--font-mono, monospace);
  font-size: 13px;
  padding: 2px 6px;
  border-radius: var(--radius-xs);
  background: var(--bg-raised);
  color: var(--brand-500);
  word-break: break-all;
}

.actions {
  display: flex;
  gap: var(--space-3);
  margin-top: var(--space-4);
}

.btn-primary {
  padding: var(--space-3) var(--space-6);
  border: none;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  color: #fff;
  cursor: pointer;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}

.btn-ghost {
  padding: var(--space-3) var(--space-6);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  font-size: 14px;
  color: var(--text-default);
  background: transparent;
  cursor: pointer;
}
.btn-ghost:hover {
  border-color: var(--border-strong);
}
</style>
