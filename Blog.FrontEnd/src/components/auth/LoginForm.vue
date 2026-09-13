<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { login } from '@/api/auth'
import { ApiError, ErrorCode } from '@/api/http'
import SiteLogo from '@/components/common/SiteLogo.vue'

const router = useRouter()
const auth = useAuthStore()

const email = ref('')
const password = ref('')
const showPassword = ref(false)
const submitting = ref(false)
const errorMsg = ref('')

const title = '登录'
// 登录前无法知道账号角色，因此副标题不区分身份
const subtitle = '登录后进入你的创作工作台或管理后台'

async function onSubmit() {
  errorMsg.value = ''

  if (!email.value.trim() || !password.value) {
    errorMsg.value = '请填写邮箱与密码'
    return
  }

  submitting.value = true
  try {
    const payload = { email: email.value.trim(), password: password.value }
    const res = await login(payload)
    auth.setSession(res)

    // 登录成功回到来源页；没有来源页时按角色进各自首页
    const returnUrl = router.currentRoute.value.query.returnUrl
    if (typeof returnUrl === 'string' && returnUrl.startsWith('/')) {
      await router.replace(returnUrl)
    } else {
      await router.replace({ name: res.role === 'Admin' ? 'admin-posts' : 'my-posts' })
    }
  } catch (e) {
    // 后端对「账号不存在/密码错误」统一返回 4010，不区分原因（防账号枚举）
    if (e instanceof ApiError && e.code === ErrorCode.Unauthorized) {
      errorMsg.value = '邮箱或密码错误'
    } else {
      errorMsg.value = e instanceof Error ? e.message : '登录失败'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="auth-page">
    <div class="aurora-blobs" />

    <form class="auth-card card" @submit.prevent="onSubmit">
      <header class="auth-head">
        <SiteLogo :size="40" />
        <h1>{{ title }}</h1>
        <p>{{ subtitle }}</p>
      </header>

      <p v-if="errorMsg" class="err-banner">{{ errorMsg }}</p>

      <label class="field">
        <span class="label">邮箱</span>
        <input
          v-model="email"
          class="input"
          type="email"
          autocomplete="username"
          placeholder="you@example.com"
          :disabled="submitting"
        />
      </label>

      <label class="field">
        <span class="label">密码</span>
        <span class="pw-wrap">
          <input
            v-model="password"
            class="input"
            :type="showPassword ? 'text' : 'password'"
            autocomplete="current-password"
            placeholder="请输入密码"
            :disabled="submitting"
          />
          <button
            type="button"
            class="pw-toggle"
            :aria-label="showPassword ? '隐藏密码' : '显示密码'"
            @click="showPassword = !showPassword"
          >
            {{ showPassword ? '隐藏' : '显示' }}
          </button>
        </span>
      </label>

      <button type="submit" class="btn-primary" :disabled="submitting">
        {{ submitting ? '登录中...' : '登录' }}
      </button>

      <footer class="auth-foot">
        <RouterLink to="/" class="link">返回首页</RouterLink>
      </footer>

      <!-- 按 T1 决策没有注册页：账号由管理员在后台创建 -->
      <p class="hint">
        没有账号？账号由管理员在「账号管理」中创建后发放。管理员与作者使用同一个登录入口。
      </p>
    </form>
  </div>
</template>

<style scoped>
.auth-page {
  position: relative;
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: var(--space-6);
  overflow: hidden;
  background: var(--bg-canvas);
}

.auth-card {
  position: relative;
  z-index: 2;
  width: 100%;
  max-width: 400px;
  padding: var(--space-8);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.auth-head {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-2);
  text-align: center;
  margin-bottom: var(--space-2);
}

.auth-head h1 {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}

.auth-head p {
  font: var(--text-caption);
  color: var(--text-subtle);
  margin: 0;
}

.err-banner {
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
  padding: var(--space-3);
  border-radius: var(--radius-sm);
  font: var(--text-body-sm);
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.label {
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}

.input {
  width: 100%;
  padding: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-strong);
  background: var(--bg-canvas);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  outline: none;
  transition: border-color var(--transition-fast);
}
.input:focus {
  border-color: var(--brand-500);
}
.input:disabled {
  opacity: 0.6;
}

.pw-wrap {
  position: relative;
  display: block;
}
.pw-wrap .input {
  padding-right: 60px;
}
.pw-toggle {
  position: absolute;
  top: 50%;
  right: var(--space-2);
  transform: translateY(-50%);
  border: none;
  background: transparent;
  color: var(--text-subtle);
  font: var(--text-caption);
  cursor: pointer;
}
.pw-toggle:hover {
  color: var(--brand-500);
}

.btn-primary {
  margin-top: var(--space-2);
  padding: var(--space-3);
  border: none;
  border-radius: var(--radius-sm);
  font-size: 15px;
  font-weight: 600;
  color: #fff;
  cursor: pointer;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}
.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.auth-foot {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  font: var(--text-caption);
  flex-wrap: wrap;
}
.muted {
  color: var(--text-subtle);
}
.dot {
  color: var(--text-disabled);
}
.link {
  color: var(--brand-500);
}
.link:hover {
  text-decoration: underline;
}

.hint {
  margin: 0;
  text-align: center;
  font: var(--text-caption);
  color: var(--text-subtle);
}
</style>
