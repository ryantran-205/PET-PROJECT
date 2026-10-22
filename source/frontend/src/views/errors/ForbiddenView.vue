<script setup lang="ts">
import ErrorPage from './ErrorPage.vue'
</script>

<template>
  <ErrorPage code="403" icon="pi-lock" title="Không có quyền truy cập" message="Tài khoản của bạn không được phép xem trang này hoặc chi nhánh này." action-label="Về trang tổng quan" action-to="/" />
</template>
