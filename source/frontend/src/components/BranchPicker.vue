<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'

/** Selects the "current branch" used by branch-scoped pages. Options are limited to branches in the user's scope. */
const auth = useAuthStore()
const options = computed(() => auth.branches.map((b) => ({ label: `${b.code} · ${b.name}`, value: b.id })))
const model = computed({
  get: () => auth.currentBranch?.id ?? null,
  set: (v: string | null) => v && auth.setBranch(v),
})
</script>

<template>
  <Select
    v-if="options.length > 1"
    v-model="model"
    :options="options"
    option-label="label"
    option-value="value"
    class="branch-picker"
    aria-label="Chọn chi nhánh"
  />
  <span v-else-if="auth.currentBranch" class="pill Confirmed">{{ auth.currentBranch.code }} · {{ auth.currentBranch.name }}</span>
</template>

<style scoped>
.branch-picker { min-width: 220px; }
</style>
