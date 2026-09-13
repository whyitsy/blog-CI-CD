<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  createUser,
  disableUser,
  getUsers,
  resetUserPassword,
  updateUser,
  type UserListItem,
} from '@/api/users'
import { getAuthors } from '@/api/authors'
import { useAuthStore } from '@/stores/auth'
import type { AuthorDto, UserRole } from '@/types'

const auth = useAuthStore()

const users = ref<UserListItem[]>([])
const authors = ref<AuthorDto[]>([])
const loading = ref(false)
const busy = ref(false)
const errorMsg = ref('')
const okMsg = ref('')

// ---------------------------------------------------------------- 新建账号
const showCreate = ref(false)
const createForm = reactive({
  email: '',
  password: '',
  role: 'Author' as UserRole,
  authorId: '' as string,
})

// ---------------------------------------------------------------- 行内编辑
const editingId = ref<string | null>(null)
const editForm = reactive({
  role: 'Author' as UserRole,
  authorId: '' as string,
  isActive: true,
})

// ---------------------------------------------------------------- 重置密码
const resetId = ref<string | null>(null)
const newPassword = ref('')

const authorName = computed(() => (id: string | null) =>
  id ? authors.value.find((a) => a.id === id)?.name ?? '(已删除的作者)' : '',
)

async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    const [u, a] = await Promise.all([getUsers(), getAuthors()])
    users.value = u
    authors.value = a
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function flash(msg: string) {
  okMsg.value = msg
  window.setTimeout(() => {
    if (okMsg.value === msg) okMsg.value = ''
  }, 2500)
}

async function onCreate() {
  errorMsg.value = ''
  if (!createForm.email.trim() || !createForm.password) {
    errorMsg.value = '邮箱与密码不能为空'
    return
  }
  if (createForm.password.length < 8) {
    errorMsg.value = '密码长度不能少于 8 位'
    return
  }

  busy.value = true
  try {
    await createUser({
      email: createForm.email.trim(),
      password: createForm.password,
      role: createForm.role,
      authorId: createForm.authorId || null,
    })
    showCreate.value = false
    createForm.email = ''
    createForm.password = ''
    createForm.role = 'Author'
    createForm.authorId = ''
    flash('账号已创建')
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '创建失败'
  } finally {
    busy.value = false
  }
}

function startEdit(u: UserListItem) {
  editingId.value = u.id
  editForm.role = u.role
  editForm.authorId = u.authorId ?? ''
  editForm.isActive = u.isActive
  errorMsg.value = ''
}

function cancelEdit() {
  editingId.value = null
}

async function saveEdit(u: UserListItem) {
  busy.value = true
  errorMsg.value = ''
  try {
    await updateUser(u.id, {
      role: editForm.role,
      authorId: editForm.authorId || null,
      isActive: editForm.isActive,
      version: u.version,
    })
    editingId.value = null
    flash('已保存')
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    busy.value = false
  }
}

async function onDisable(u: UserListItem) {
  if (!window.confirm(`确认停用账号「${u.email}」？停用后无法登录，且其已签发的 token 立即失效。`)) return
  busy.value = true
  errorMsg.value = ''
  try {
    await disableUser(u.id, u.version)
    flash('已停用')
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '停用失败'
  } finally {
    busy.value = false
  }
}

async function onResetPassword(u: UserListItem) {
  errorMsg.value = ''
  if (newPassword.value.length < 8) {
    errorMsg.value = '密码长度不能少于 8 位'
    return
  }
  busy.value = true
  try {
    await resetUserPassword(u.id, newPassword.value, u.version)
    resetId.value = null
    newPassword.value = ''
    flash('密码已重置，该账号需用新密码重新登录')
    await load()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '重置失败'
  } finally {
    busy.value = false
  }
}

function fmt(iso: string | null) {
  return iso ? iso.replace('T', ' ').slice(0, 16) : '—'
}

/** 不允许停用自己，避免把自己锁在门外 */
function isSelf(u: UserListItem) {
  return u.id === auth.user?.id
}
</script>

<template>
  <section class="admin-users">
    <div class="toolbar">
      <span class="toolbar-info">共 {{ users.length }} 个账号</span>
      <button class="btn-primary" @click="showCreate = !showCreate">
        {{ showCreate ? '收起' : '+ 新建账号' }}
      </button>
    </div>

    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>
    <p v-if="okMsg" class="banner ok">{{ okMsg }}</p>

    <!-- 新建账号 -->
    <form v-if="showCreate" class="card panel" @submit.prevent="onCreate">
      <h3>新建账号</h3>
      <p class="panel-hint">
        作者账号由此创建（系统不开放自助注册）。创建后把邮箱与初始密码告知作者。
      </p>

      <div class="grid">
        <label class="field">
          <span class="label">邮箱</span>
          <input v-model="createForm.email" class="input" type="email" placeholder="author@example.com" />
        </label>

        <label class="field">
          <span class="label">初始密码</span>
          <input
            v-model="createForm.password"
            class="input"
            type="password"
            placeholder="至少 8 位"
            autocomplete="new-password"
          />
        </label>

        <label class="field">
          <span class="label">角色</span>
          <select v-model="createForm.role" class="input">
            <option value="Author">Author（内容作者）</option>
            <option value="Admin">Admin（站点管理员）</option>
          </select>
        </label>

        <label class="field">
          <span class="label">关联作者（内容署名）</span>
          <select v-model="createForm.authorId" class="input">
            <option value="">不关联</option>
            <option v-for="a in authors" :key="a.id" :value="a.id">{{ a.name }}</option>
          </select>
          <span class="hint">
            角色为 Author 时建议关联：决定他发布文章的署名对象（不是登录身份）
          </span>
        </label>
      </div>

      <footer class="panel-actions">
        <button type="button" class="btn-ghost" @click="showCreate = false">取消</button>
        <button type="submit" class="btn-primary" :disabled="busy">创建</button>
      </footer>
    </form>

    <div v-if="loading" class="muted-block">加载中...</div>

    <div v-else class="list card">
      <div class="row head-row">
        <span>邮箱</span>
        <span>角色</span>
        <span>关联作者</span>
        <span>状态</span>
        <span>最近登录</span>
        <span class="right">操作</span>
      </div>

      <div v-for="u in users" :key="u.id" class="row">
        <template v-if="editingId === u.id">
          <span class="col-email">{{ u.email }}</span>
          <select v-model="editForm.role" class="input mini">
            <option value="Author">Author</option>
            <option value="Admin">Admin</option>
          </select>
          <select v-model="editForm.authorId" class="input mini">
            <option value="">不关联</option>
            <option v-for="a in authors" :key="a.id" :value="a.id">{{ a.name }}</option>
          </select>
          <label class="switch">
            <input v-model="editForm.isActive" type="checkbox" />
            <span>{{ editForm.isActive ? '启用' : '停用' }}</span>
          </label>
          <span class="muted">v{{ u.version }}</span>
          <span class="right actions">
            <button class="link" :disabled="busy" @click="saveEdit(u)">保存</button>
            <button class="link muted-link" :disabled="busy" @click="cancelEdit">取消</button>
          </span>
        </template>

        <template v-else>
          <span class="col-email">
            {{ u.email }}
            <span v-if="isSelf(u)" class="self-tag">我自己</span>
          </span>
          <span>
            <span class="role-badge" :class="u.role === 'Admin' ? 'admin' : 'author'">{{ u.role }}</span>
          </span>
          <span class="muted">{{ authorName(u.authorId) || '—' }}</span>
          <span>
            <span v-if="u.isActive" class="badge on">启用</span>
            <span v-else class="badge off">已停用</span>
          </span>
          <span class="muted">{{ fmt(u.lastLoginAt) }}</span>
          <span class="right actions">
            <button class="link" :disabled="busy" @click="startEdit(u)">编辑</button>
            <button class="link" :disabled="busy" @click="resetId = resetId === u.id ? null : u.id">
              重置密码
            </button>
            <button
              v-if="!isSelf(u) && u.isActive"
              class="link danger"
              :disabled="busy"
              @click="onDisable(u)"
            >
              停用
            </button>
          </span>
        </template>

        <!-- 重置密码行 -->
        <span v-if="resetId === u.id" class="reset-row">
          <input
            v-model="newPassword"
            class="input mini"
            type="password"
            placeholder="新密码（至少 8 位）"
            autocomplete="new-password"
          />
          <button class="link" :disabled="busy" @click="onResetPassword(u)">确认重置</button>
          <button class="link muted-link" @click="((resetId = null), (newPassword = ''))">取消</button>
          <span class="hint">重置后该账号所有已签发 token 立即失效</span>
        </span>
      </div>
    </div>

    <p class="foot-hint">
      说明：本页只管理**登录账号**。文章的署名对象（Author）是内容，属「内容层」，
      需要单独的「作者管理」页面（见 archive/决策记录.md §2.3（T14b））。
    </p>
  </section>
</template>

<style scoped>
.admin-users {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.toolbar-info {
  font: var(--text-body-sm);
  color: var(--text-muted);
}

.banner {
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-sm);
  font: var(--text-body-sm);
}
.banner.err {
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
}
.banner.ok {
  background: color-mix(in srgb, #16a34a 14%, transparent);
  color: #16a34a;
}

.panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-6);
}
.panel h3 {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}
.panel-hint {
  font: var(--text-caption);
  color: var(--text-subtle);
  margin: 0;
}
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--space-4);
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
.hint {
  font: var(--text-caption);
  color: var(--text-subtle);
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
}
.input:focus {
  border-color: var(--brand-500);
}
.input.mini {
  padding: var(--space-2);
  font-size: 13px;
}

.panel-actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
}

.muted-block {
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}

.list {
  overflow: hidden;
}
.row {
  display: grid;
  grid-template-columns: minmax(0, 1.4fr) 90px 110px 80px 130px 230px;
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
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}

.col-email {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-strong);
  font-weight: 600;
}
.self-tag {
  flex-shrink: 0;
  font: var(--text-caption);
  padding: 1px 6px;
  border-radius: 999px;
  color: var(--brand-500);
  background: color-mix(in srgb, var(--brand-500) 14%, transparent);
}

.role-badge {
  font: var(--text-caption);
  padding: 2px 8px;
  border-radius: 999px;
}
.role-badge.admin {
  color: #a855f7;
  background: color-mix(in srgb, #a855f7 16%, transparent);
}
.role-badge.author {
  color: #22c5dc;
  background: color-mix(in srgb, #22c5dc 16%, transparent);
}

.badge {
  font: var(--text-caption);
  padding: 2px 8px;
  border-radius: 999px;
}
.badge.on {
  color: #16a34a;
  background: color-mix(in srgb, #16a34a 18%, transparent);
}
.badge.off {
  color: #e35151;
  background: color-mix(in srgb, #e35151 18%, transparent);
}

.muted {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.right {
  text-align: right;
}
.actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
  flex-wrap: wrap;
}

.switch {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font: var(--text-caption);
  color: var(--text-muted);
  cursor: pointer;
}

.reset-row {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
  padding-top: var(--space-2);
  border-top: 1px dashed var(--border-subtle);
}

.btn-primary {
  padding: var(--space-2) var(--space-4);
  border: none;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  color: #fff;
  cursor: pointer;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}
.btn-ghost {
  padding: var(--space-2) var(--space-4);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-default);
  cursor: pointer;
}
.btn-primary:disabled,
.btn-ghost:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.link {
  border: none;
  background: none;
  padding: 0;
  font-size: 13px;
  color: var(--brand-500);
  cursor: pointer;
  white-space: nowrap;
}
.link.muted-link {
  color: var(--text-subtle);
}
.link.danger {
  color: #e35151;
}
.link:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.foot-hint {
  margin: 0;
  font: var(--text-caption);
  color: var(--text-subtle);
  line-height: 1.8;
}

@media (max-width: 1100px) {
  .head-row {
    display: none;
  }
  .row {
    grid-template-columns: 1fr auto;
    gap: var(--space-2) var(--space-3);
  }
  .col-email {
    grid-column: 1 / -1;
    font-size: 15px;
  }
  .right {
    text-align: left;
  }
  .actions {
    grid-column: 1 / -1;
    justify-content: flex-start;
    padding-top: var(--space-2);
    border-top: 1px dashed var(--border-subtle);
  }
}
</style>
