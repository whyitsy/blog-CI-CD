<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterView } from 'vue-router'
import NavBar from '@/components/common/NavBar.vue'
import SiteFooter from '@/components/common/SiteFooter.vue'
import SearchModal from '@/components/search/SearchModal.vue'
import { useSiteStore } from '@/stores/site'

const site = useSiteStore()
onMounted(() => site.ensureLoaded())
</script>

<template>
  <NavBar />
  <main class="layout-main">
    <RouterView v-slot="{ Component }">
      <Transition name="fade" mode="out-in">
        <component :is="Component" />
      </Transition>
    </RouterView>
  </main>
  <SiteFooter />
  <SearchModal />
</template>

<style scoped>
.layout-main {
  flex: 1;
  display: flex;
  flex-direction: column;
}
</style>
