import { createRouter, createWebHistory } from 'vue-router'

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
      ],
    },

    // 管理端：独立 AdminLayout（自带侧边栏，不套用公开站点的 NavBar / Footer）
    // 无鉴权，本地单人博客场景；上线前应加认证
    {
      path: '/admin',
      component: () => import('@/layouts/AdminLayout.vue'),
      children: [
        { path: '', name: 'admin-posts', component: () => import('@/views/AdminPostListView.vue') },
        { path: 'posts/new', name: 'admin-post-new', component: () => import('@/views/AdminPostNewView.vue') },
        { path: 'posts/:id/edit', name: 'admin-post-edit', component: () => import('@/views/AdminPostEditView.vue') },
        { path: 'categories', name: 'admin-categories', component: () => import('@/views/AdminCategoryListView.vue') },
        { path: 'tags', name: 'admin-tags', component: () => import('@/views/AdminTagListView.vue') },
        { path: 'profile', name: 'admin-profile', component: () => import('@/views/AdminProfileView.vue') },
        { path: 'site', name: 'admin-site', component: () => import('@/views/AdminSiteConfigView.vue') },
      ],
    },

    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
  scrollBehavior(_to, _from, savedPosition) {
    return savedPosition ?? { top: 0 }
  },
})

export default router
