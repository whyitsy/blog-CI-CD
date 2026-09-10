<script setup lang="ts">
import TaxonomyManager from '@/components/admin/TaxonomyManager.vue'
import { createTag, deleteTag, getTags, updateTag } from '@/api/tags'
</script>

<template>
  <TaxonomyManager
    kind="tag"
    show-post-count
    :load="getTags"
    :create="createTag"
    :update="updateTag"
    :remove="deleteTag"
  />
</template>
