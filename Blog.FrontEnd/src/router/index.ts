import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: () => import('@/views/HomeView.vue') },
    { path: '/post/:id', name: 'post-detail', component: () => import('@/views/PostDetailView.vue') },
    { path: '/tags', name: 'tags', component: () => import('@/views/TagsView.vue') },
    { path: '/categories', name: 'categories', component: () => import('@/views/CategoriesView.vue') },
    { path: '/archive', name: 'archive', component: () => import('@/views/ArchiveView.vue') },
    { path: '/posts', name: 'post-list', component: () => import('@/views/PostListView.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
  scrollBehavior(_to, _from, savedPosition) {
    return savedPosition ?? { top: 0 }
  },
})

export default router
