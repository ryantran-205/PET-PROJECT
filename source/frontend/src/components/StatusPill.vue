<script setup lang="ts">
import { computed } from 'vue'
import { statusLabels } from '@/utils/format'

const props = defineProps<{ status: string | null | undefined }>()
const label = computed(() => (props.status ? statusLabels[props.status] ?? props.status : '—'))
</script>

<template>
  <span class="pill" :class="status ?? ''">{{ label }}</span>
</template>
