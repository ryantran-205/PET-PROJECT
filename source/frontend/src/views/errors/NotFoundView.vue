<script setup lang="ts">
import ErrorPage from './ErrorPage.vue'
</script>

<template>
  <ErrorPage code="404" icon="pi-compass" title="Không tìm thấy trang" message="Đường dẫn không tồn tại hoặc đã được thay đổi." action-label="Về trang chủ" action-to="/" />
</template>
