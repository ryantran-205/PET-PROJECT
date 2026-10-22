<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import ErrorPage from './ErrorPage.vue'

const route = useRoute()
const to = computed(() => {
  const r = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
  return `/login?redirect=${encodeURIComponent(r)}`
})
</script>

<template>
  <ErrorPage code="401" icon="pi-hourglass" title="Phiên đăng nhập đã hết hạn" message="Vì an toàn, vui lòng đăng nhập lại để tiếp tục." action-label="Đăng nhập lại" :action-to="to" />
</template>
