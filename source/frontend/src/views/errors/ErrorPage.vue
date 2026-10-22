<script setup lang="ts">
defineProps<{ code: string; icon: string; title: string; message: string; actionLabel: string; actionTo: string }>()
</script>

<template>
  <div class="err-wrap">
    <div class="card err-card pop-in">
      <div class="code gradient-text">{{ code }}</div>
      <i class="pi" :class="icon" />
      <h1>{{ title }}</h1>
      <p class="muted">{{ message }}</p>
      <RouterLink :to="actionTo"><Button :label="actionLabel" icon="pi pi-arrow-left" /></RouterLink>
    </div>
  </div>
</template>

<style scoped>
.err-wrap { min-height: 100vh; display: grid; place-items: center; padding: 24px; }
.err-card { text-align: center; max-width: 440px; padding: 40px 32px; }
.code { font-size: 4rem; font-weight: 900; line-height: 1; }
.err-card .pi { font-size: 1.6rem; color: var(--text-faint); margin: 14px 0 6px; display: block; }
.err-card p { margin: 8px 0 22px; }
</style>
