<script setup lang="ts">
import { ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useRouter } from 'vue-router'
import { api, errorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { roleLabels } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const router = useRouter()
const currentPassword = ref('')
const newPassword = ref('')
const confirm = ref('')
const saving = ref(false)
const error = ref('')

async function changePassword() {
  error.value = ''
  if (newPassword.value !== confirm.value) {
    error.value = 'Mật khẩu xác nhận không khớp.'
    return
  }
  saving.value = true
  try {
    await api.post('/me/change-password', { currentPassword: currentPassword.value, newPassword: newPassword.value })
    toast.add({ severity: 'success', summary: 'Đã đổi mật khẩu', detail: 'Vui lòng đăng nhập lại.', life: 4000 })
    await auth.logout()
    router.replace({ name: 'login' })
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Tài khoản</h1>
        <p>Thông tin đăng nhập và phạm vi chi nhánh.</p>
      </div>
    </div>
    <div class="grid grid-2">
      <div class="card">
        <div class="card-title"><h2>Thông tin</h2></div>
        <div class="stack">
          <div><span class="muted">Họ tên:</span> {{ auth.me?.displayName }}</div>
          <div><span class="muted">Tên đăng nhập:</span> <span class="mono">{{ auth.me?.userName }}</span></div>
          <div><span class="muted">Vai trò:</span> {{ roleLabels[auth.role ?? ''] }}</div>
          <div class="row wrap">
            <span class="muted">Chi nhánh:</span>
            <span v-for="b in auth.branches" :key="b.id" class="pill Confirmed">{{ b.code }} · {{ b.name }}</span>
          </div>
        </div>
      </div>
      <form class="card" @submit.prevent="changePassword">
        <div class="card-title"><h2>Đổi mật khẩu</h2></div>
        <div class="stack">
          <div class="field">
            <label for="acc-current">Mật khẩu hiện tại</label>
            <Password input-id="acc-current" v-model="currentPassword" :feedback="false" toggle-mask fluid autocomplete="current-password" />
          </div>
          <div class="field">
            <label for="acc-new">Mật khẩu mới (≥ 8 ký tự, có chữ hoa, chữ thường, số)</label>
            <Password input-id="acc-new" v-model="newPassword" toggle-mask fluid autocomplete="new-password" />
          </div>
          <div class="field">
            <label for="acc-confirm">Nhập lại mật khẩu mới</label>
            <Password input-id="acc-confirm" v-model="confirm" :feedback="false" toggle-mask fluid autocomplete="new-password" />
          </div>
          <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
          <div><Button id="btn-change-password" type="submit" label="Cập nhật mật khẩu" icon="pi pi-key" :loading="saving" /></div>
        </div>
      </form>
    </div>
  </div>
</template>
