<script setup lang="ts">
import LoginForm from '@/components/auth/LoginForm.vue'
</script>

<template>
  <!--
    单一登录入口：管理员与作者共用。
    登录成功后由 LoginForm 按响应里的 role 跳转到 /admin/posts 或 /me。
  -->
  <LoginForm />
</template>
