<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { ApiError, errorMessage } from '@/api/http'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const userName = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

async function submit() {
  error.value = ''
  if (!userName.value || !password.value) {
    error.value = 'Vui lòng nhập tên đăng nhập và mật khẩu.'
    return
  }
  loading.value = true
  try {
    await auth.login(userName.value.trim(), password.value)
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/') ? route.query.redirect : '/'
    router.replace(redirect)
  } catch (e) {
    error.value = e instanceof ApiError && e.status === 429 ? 'Bạn đã thử quá nhiều lần. Vui lòng đợi một chút rồi thử lại.' : errorMessage(e)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-wrap">
    <section class="hero">
      <div class="glow" />
      <div class="hero-inner">
        <div class="logo"><i class="pi pi-sparkles" /></div>
        <h1>Vận hành chuỗi F&amp;B <span class="gradient-text">mượt mà</span> từ quầy tới kho.</h1>
        <p class="muted">Đơn QR thời gian thực, trừ kho theo định mức, chấm công, hóa đơn PDF và báo cáo doanh thu — tất cả trong một nơi.</p>
        <ul class="features">
          <li><i class="pi pi-bolt" /> Đơn mới hiện ngay trên bảng pha chế</li>
          <li><i class="pi pi-box" /> Sổ kho bất biến, không âm kho</li>
          <li><i class="pi pi-shield" /> Phân quyền theo chi nhánh</li>
        </ul>
      </div>
    </section>

    <section class="form-side">
      <form class="card login-card pop-in" @submit.prevent="submit" novalidate>
        <h2>Đăng nhập</h2>
        <p class="muted small" style="margin: 4px 0 20px">Dành cho quản trị viên và nhân viên cửa hàng.</p>
        <div class="stack">
          <div class="field">
            <label for="login-username">Tên đăng nhập</label>
            <InputText id="login-username" v-model="userName" autocomplete="username" autofocus />
          </div>
          <div class="field">
            <label for="login-password">Mật khẩu</label>
            <Password input-id="login-password" v-model="password" :feedback="false" toggle-mask autocomplete="current-password" fluid />
          </div>
          <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
          <Button id="btn-login" type="submit" label="Đăng nhập" icon="pi pi-arrow-right" icon-pos="right" :loading="loading" class="w-full" />
        </div>
      </form>
    </section>
  </div>
</template>

<style scoped>
.login-wrap { display: grid; grid-template-columns: 1.15fr 1fr; min-height: 100vh; }
.hero { position: relative; overflow: hidden; display: flex; align-items: center; padding: 64px; border-right: 1px solid var(--border); }
.glow { position: absolute; inset: -30%; background: radial-gradient(closest-side, rgba(255, 138, 61, .25), transparent 70%); animation: drift 12s ease-in-out infinite alternate; }
@keyframes drift { from { transform: translate(-8%, -6%); } to { transform: translate(10%, 8%); } }
.hero-inner { position: relative; max-width: 560px; }
.hero h1 { font-size: 2.6rem; line-height: 1.15; margin: 24px 0 16px; }
.logo { width: 56px; height: 56px; border-radius: 16px; display: grid; place-items: center; background: var(--accent-grad); color: #1b120d; font-size: 1.5rem; box-shadow: var(--shadow-glow); }
.features { list-style: none; padding: 0; margin: 28px 0 0; display: grid; gap: 12px; }
.features li { display: flex; gap: 12px; align-items: center; color: var(--text-muted); }
.features .pi { color: var(--accent); background: var(--accent-soft); padding: 8px; border-radius: 10px; }
.form-side { display: grid; place-items: center; padding: 32px; }
.login-card { width: 100%; max-width: 400px; padding: 32px; }
@media (max-width: 900px) { .login-wrap { grid-template-columns: 1fr; } .hero { display: none; } }
</style>
