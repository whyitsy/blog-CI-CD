<script setup lang="ts">
import TaxonomyManager from '@/components/admin/TaxonomyManager.vue'
import { createCategory, deleteCategory, getCategories, updateCategory } from '@/api/categories'
</script>

<template>
  <TaxonomyManager
    kind="category"
    show-post-count
    :load="getCategories"
    :create="createCategory"
    :update="updateCategory"
    :remove="deleteCategory"
  />
</template>
