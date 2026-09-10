import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import type { UserRole } from '@/types'

declare module 'vue-router' {
  interface RouteMeta {
    /** 需要登录 */
    requiresAuth?: boolean
    /** 允许的角色；不填则任何已登录用户都可访问 */
    roles?: UserRole[]
    /** 仅未登录可访问（登录/注册页），已登录会被送回首页 */
    guestOnly?: boolean
  }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    // 公开站点：统一使用 DefaultLayout（NavBar + Footer + 搜索弹窗）
    {
      path: '/',
      component: () => import('@/layouts/DefaultLayout.vue'),
      children: [
        { path: '', name: 'home', component: () => import('@/views/HomeView.vue') },
        { path: 'post/:id', name: 'post-detail', component: () => import('@/views/PostDetailView.vue') },
        { path: 'tags', name: 'tags', component: () => import('@/views/TagsView.vue') },
        { path: 'categories', name: 'categories', component: () => import('@/views/CategoriesView.vue') },
        { path: 'archive', name: 'archive', component: () => import('@/views/ArchiveView.vue') },
        { path: 'posts', name: 'post-list', component: () => import('@/views/PostListView.vue') },
        { path: 'collections', name: 'collections', component: () => import('@/views/CollectionListView.vue') },
        { path: 'collections/:slug', name: 'collection-detail', component: () => import('@/views/CollectionDetailView.vue') },
      ],
    },

    // 认证页：极简独立布局（不套 NavBar/Footer/AdminLayout）
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/auth/AuthorLoginView.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/admin/login',
      name: 'admin-login',
      component: () => import('@/views/auth/AdminLoginView.vue'),
      meta: { guestOnly: true },
    },

    // 作者工作区：需要登录，Admin 也可进入（便于帮作者处理）
    {
      path: '/me',
      component: () => import('@/layouts/AuthorLayout.vue'),
      meta: { requiresAuth: true, roles: ['Author', 'Admin'] },
      children: [
        { path: '', name: 'my-posts', component: () => import('@/views/me/MyPostListView.vue') },
        { path: 'posts/new', name: 'my-post-new', component: () => import('@/views/me/MyPostNewView.vue') },
        { path: 'posts/:id/edit', name: 'my-post-edit', component: () => import('@/views/me/MyPostEditView.vue') },
      ],
    },

    // 管理后台：独立 AdminLayout，仅管理员
    {
      path: '/admin',
      component: () => import('@/layouts/AdminLayout.vue'),
      meta: { requiresAuth: true, roles: ['Admin'] },
      children: [
        { path: '', name: 'admin-posts', component: () => import('@/views/AdminPostListView.vue') },
        { path: 'posts/new', name: 'admin-post-new', component: () => import('@/views/AdminPostNewView.vue') },
        { path: 'posts/:id/edit', name: 'admin-post-edit', component: () => import('@/views/AdminPostEditView.vue') },
        { path: 'categories', name: 'admin-categories', component: () => import('@/views/AdminCategoryListView.vue') },
        { path: 'tags', name: 'admin-tags', component: () => import('@/views/AdminTagListView.vue') },
        { path: 'users', name: 'admin-users', component: () => import('@/views/AdminUserListView.vue') },
        { path: 'collections', name: 'admin-collections', component: () => import('@/views/AdminCollectionListView.vue') },
        { path: 'profile', name: 'admin-profile', component: () => import('@/views/AdminProfileView.vue') },
        { path: 'site', name: 'admin-site', component: () => import('@/views/AdminSiteConfigView.vue') },
      ],
    },

    // 404：真实的不存在页面（此前是静默重定向首页，会产生软 404，见 docs/frontend.md F5）
    { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/NotFoundView.vue') },
  ],
  scrollBehavior(_to, _from, savedPosition) {
    return savedPosition ?? { top: 0 }
  },
})

/**
 * 路由守卫。
 *
 * 重要：这只是**前端体验**控制，可被绕过（改 JS 即可）。
 * 真正的安全边界在后端 —— 所有受保护操作后端都会再校验一次角色与资源归属。
 */
router.beforeEach((to) => {
  const auth = useAuthStore()

  // 仅未登录可访问（登录页）：已登录则送回各自首页
  if (to.meta.guestOnly && auth.isAuthenticated) {
    return auth.isAdmin ? { name: 'admin-posts' } : { name: 'my-posts' }
  }

  if (!to.meta.requiresAuth) return true

  if (!auth.isAuthenticated) {
    return {
      name: to.path.startsWith('/admin') ? 'admin-login' : 'login',
      query: { returnUrl: to.fullPath },
    }
  }

  const allowed = to.meta.roles
  if (allowed && auth.role && !allowed.includes(auth.role)) {
    // 已登录但角色不足：回到自己的地盘，而不是报错页
    return auth.isAdmin ? { name: 'admin-posts' } : { name: 'my-posts' }
  }

  return true
})

export default router
