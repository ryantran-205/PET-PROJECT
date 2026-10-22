<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import type { Role } from '@/api/types'
import { roleLabels } from '@/utils/format'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const mobileOpen = ref(false)

interface NavItem { to: string; label: string; icon: string; roles?: Role[] }
interface NavGroup { title: string; items: NavItem[] }
const ADMINS: Role[] = ['SuperAdmin', 'BranchAdmin']

const groups: NavGroup[] = [
  {
    title: 'Vận hành',
    items: [
      { to: '/', label: 'Tổng quan', icon: 'pi-chart-bar' },
      { to: '/orders', label: 'Đơn hàng', icon: 'pi-receipt' },
      { to: '/kitchen', label: 'Bảng pha chế', icon: 'pi-th-large' },
      { to: '/invoices', label: 'Hóa đơn', icon: 'pi-file-pdf' },
    ],
  },
  {
    title: 'Thực đơn & Kho',
    items: [
      { to: '/menu', label: 'Thực đơn', icon: 'pi-book' },
      { to: '/branch-menu', label: 'Menu chi nhánh', icon: 'pi-sliders-h' },
      { to: '/recipes', label: 'Định mức', icon: 'pi-sitemap', roles: ['SuperAdmin'] },
      { to: '/inventory', label: 'Tồn kho', icon: 'pi-box' },
      { to: '/inventory/ledger', label: 'Sổ kho', icon: 'pi-history' },
    ],
  },
  {
    title: 'Nhân sự',
    items: [
      { to: '/attendance', label: 'Chấm công', icon: 'pi-clock' },
      { to: '/shifts', label: 'Ca làm', icon: 'pi-calendar' },
      { to: '/leave', label: 'Nghỉ phép', icon: 'pi-calendar-minus' },
      { to: '/employees', label: 'Nhân viên', icon: 'pi-id-card', roles: ADMINS },
      { to: '/payroll', label: 'Bảng công', icon: 'pi-wallet', roles: ADMINS },
    ],
  },
  {
    title: 'Quản trị',
    items: [
      { to: '/reports', label: 'Báo cáo', icon: 'pi-chart-line', roles: ADMINS },
      { to: '/branches', label: 'Chi nhánh', icon: 'pi-building', roles: ADMINS },
      { to: '/users', label: 'Người dùng', icon: 'pi-users', roles: ADMINS },
      { to: '/audit', label: 'Nhật ký', icon: 'pi-shield', roles: ADMINS },
    ],
  },
]

const visibleGroups = computed(() =>
  groups
    .map((g) => ({ ...g, items: g.items.filter((i) => !i.roles || auth.hasRole(...i.roles)) }))
    .filter((g) => g.items.length),
)

function isActive(to: string) {
  return to === '/' ? route.path === '/' : route.path === to || (route.path.startsWith(to + '/') && to !== '/inventory')
}

const initials = computed(() =>
  (auth.me?.displayName ?? '?')
    .split(' ')
    .filter(Boolean)
    .slice(-2)
    .map((s) => s[0])
    .join('')
    .toUpperCase(),
)

watch(() => route.fullPath, () => (mobileOpen.value = false))

async function logout() {
  await auth.logout()
  router.replace({ name: 'login' })
}
</script>

<template>
  <div class="shell" :class="{ open: mobileOpen }">
    <aside class="sidebar">
      <div class="brand">
        <div class="logo"><i class="pi pi-sparkles" /></div>
        <div>
          <div class="brand-name">Brewline</div>
          <div class="brand-sub">F&amp;B Operations</div>
        </div>
      </div>

      <nav aria-label="Điều hướng chính">
        <div v-for="g in visibleGroups" :key="g.title" class="nav-group">
          <div class="nav-title">{{ g.title }}</div>
          <RouterLink v-for="i in g.items" :key="i.to" :to="i.to" class="nav-link" :class="{ active: isActive(i.to) }">
            <i class="pi" :class="i.icon" />
            <span>{{ i.label }}</span>
          </RouterLink>
        </div>
      </nav>

      <div class="user-card">
        <Avatar :label="initials" shape="circle" class="avatar" />
        <div class="user-meta">
          <RouterLink to="/account" class="user-name">{{ auth.me?.displayName }}</RouterLink>
          <div class="faint small">{{ roleLabels[auth.role ?? ''] }}</div>
        </div>
        <Button id="btn-logout" icon="pi pi-sign-out" text rounded severity="secondary" v-tooltip.top="'Đăng xuất'" @click="logout" />
      </div>
    </aside>

    <div class="backdrop" @click="mobileOpen = false" />

    <div class="main">
      <header class="topbar">
        <Button class="menu-btn" icon="pi pi-bars" text rounded aria-label="Mở menu" @click="mobileOpen = !mobileOpen" />
        <div class="crumb">{{ route.meta.title }}</div>
        <div class="spacer" />
        <BranchPicker />
      </header>
      <main>
        <RouterView v-slot="{ Component }">
          <component :is="Component" :key="(auth.currentBranch?.id ?? '') + route.path" />
        </RouterView>
      </main>
    </div>
  </div>
</template>

<style scoped>
.shell { display: flex; min-height: 100vh; }
.sidebar {
  position: sticky; top: 0; height: 100vh; width: var(--sidebar-w); flex-shrink: 0;
  display: flex; flex-direction: column; gap: 10px; padding: 18px 14px;
  background: linear-gradient(180deg, rgba(30, 21, 17, .92), rgba(18, 13, 10, .96));
  border-right: 1px solid var(--border); backdrop-filter: blur(16px); z-index: 30;
}
.brand { display: flex; gap: 12px; align-items: center; padding: 6px 10px 16px; }
.logo {
  width: 40px; height: 40px; border-radius: 12px; display: grid; place-items: center;
  background: var(--accent-grad); color: #1b120d; font-size: 1.1rem; box-shadow: var(--shadow-glow);
}
.brand-name { font-weight: 800; font-size: 1.15rem; letter-spacing: -0.02em; }
.brand-sub { font-size: .72rem; color: var(--text-faint); text-transform: uppercase; letter-spacing: .12em; }
nav { flex: 1; overflow-y: auto; padding-right: 2px; }
.nav-group { margin-bottom: 14px; }
.nav-title { font-size: .7rem; text-transform: uppercase; letter-spacing: .12em; color: var(--text-faint); padding: 6px 12px; }
.nav-link {
  display: flex; align-items: center; gap: 12px; padding: 9px 12px; border-radius: 10px; color: var(--text-muted);
  font-weight: 500; transition: background .15s, color .15s, transform .15s; position: relative;
}
.nav-link:hover { background: var(--surface-hover); color: var(--text); text-decoration: none; transform: translateX(2px); }
.nav-link.active { background: var(--accent-soft); color: var(--text); }
.nav-link.active::before {
  content: ''; position: absolute; left: -14px; top: 8px; bottom: 8px; width: 3px; border-radius: 3px; background: var(--accent-grad);
}
.nav-link.active .pi { color: var(--accent); }
.user-card { display: flex; align-items: center; gap: 10px; padding: 10px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); }
.avatar { background: var(--accent-grad) !important; color: #1b120d !important; font-weight: 700; }
.user-meta { flex: 1; min-width: 0; }
.user-name { color: var(--text); font-weight: 600; display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.topbar {
  position: sticky; top: 0; z-index: 20; display: flex; align-items: center; gap: 12px; padding: 12px 32px;
  background: rgba(18, 13, 10, .7); backdrop-filter: blur(14px); border-bottom: 1px solid var(--border);
}
.crumb { font-weight: 600; color: var(--text-muted); }
.menu-btn { display: none; }
.backdrop { display: none; }
@media (max-width: 960px) {
  .sidebar { position: fixed; left: 0; transform: translateX(-100%); transition: transform .25s ease; }
  .shell.open .sidebar { transform: none; }
  .shell.open .backdrop { display: block; position: fixed; inset: 0; background: rgba(0, 0, 0, .5); z-index: 25; }
  .menu-btn { display: inline-flex; }
  .topbar { padding: 10px 14px; }
}
</style>
