<script setup lang="ts">
import { onSessionExpired } from '@/api/http'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

// Refresh token rejected/expired → drop the local session and send the user to the session-expired page.
onSessionExpired(() => {
  auth.me = null
  const current = router.currentRoute.value
  if (current.meta.requiresAuth) router.replace({ name: 'session-expired', query: { redirect: current.fullPath } })
})
</script>

<template>
  <Toast position="top-right" />
  <ConfirmDialog />
  <RouterView />
</template>
