<script setup lang="ts">
import { computed } from 'vue'
import { money } from '@/utils/format'

const props = defineProps<{ value: number | null | undefined; strong?: boolean }>()
const text = computed(() => money(props.value))
</script>

<template>
  <span class="nowrap" :style="strong ? 'font-weight:700' : undefined">{{ text }}</span>
</template>
