<script setup lang="ts">
withDefaults(defineProps<{ icon?: string; title?: string; message?: string }>(), {
  icon: 'pi-inbox',
  title: 'Chưa có dữ liệu',
  message: '',
})
</script>

<template>
  <div class="empty">
    <i class="pi" :class="icon" />
    <div style="font-weight: 600; color: var(--text)">{{ title }}</div>
    <div v-if="message" class="small" style="margin-top: 4px">{{ message }}</div>
    <div style="margin-top: 12px"><slot /></div>
  </div>
</template>
