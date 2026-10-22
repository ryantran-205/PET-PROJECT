import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import type { Role } from '@/api/types'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    roles?: Role[]
    title?: string
  }
}

const ADMINS: Role[] = ['SuperAdmin', 'BranchAdmin']

const routes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: () => import('@/views/auth/LoginView.vue'), meta: { title: 'Đăng nhập' } },
  {
    path: '/',
    component: () => import('@/layouts/AdminLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      { path: '', name: 'dashboard', component: () => import('@/views/admin/DashboardView.vue'), meta: { title: 'Tổng quan' } },
      { path: 'orders', name: 'orders', component: () => import('@/views/orders/OrdersView.vue'), meta: { title: 'Đơn hàng' } },
      { path: 'kitchen', name: 'kitchen', component: () => import('@/views/orders/KitchenView.vue'), meta: { title: 'Bảng pha chế' } },
      { path: 'invoices', name: 'invoices', component: () => import('@/views/reports/InvoicesView.vue'), meta: { title: 'Hóa đơn' } },
      { path: 'menu', name: 'menu', component: () => import('@/views/menu/MenuView.vue'), meta: { title: 'Thực đơn' } },
      { path: 'branch-menu', name: 'branch-menu', component: () => import('@/views/menu/BranchMenuView.vue'), meta: { title: 'Menu chi nhánh' } },
      { path: 'recipes', name: 'recipes', component: () => import('@/views/menu/RecipesView.vue'), meta: { title: 'Định mức', roles: ['SuperAdmin'] } },
      { path: 'inventory', name: 'inventory', component: () => import('@/views/inventory/InventoryView.vue'), meta: { title: 'Kho' } },
      { path: 'inventory/ledger', name: 'ledger', component: () => import('@/views/inventory/LedgerView.vue'), meta: { title: 'Sổ kho' } },
      { path: 'attendance', name: 'attendance', component: () => import('@/views/hr/AttendanceView.vue'), meta: { title: 'Chấm công' } },
      { path: 'leave', name: 'leave', component: () => import('@/views/hr/LeaveView.vue'), meta: { title: 'Nghỉ phép' } },
      { path: 'shifts', name: 'shifts', component: () => import('@/views/hr/ShiftsView.vue'), meta: { title: 'Ca làm' } },
      { path: 'employees', name: 'employees', component: () => import('@/views/hr/EmployeesView.vue'), meta: { title: 'Nhân viên', roles: ADMINS } },
      { path: 'payroll', name: 'payroll', component: () => import('@/views/hr/PayrollView.vue'), meta: { title: 'Bảng công', roles: ADMINS } },
      { path: 'reports', name: 'reports', component: () => import('@/views/reports/ReportsView.vue'), meta: { title: 'Báo cáo', roles: ADMINS } },
      { path: 'branches', name: 'branches', component: () => import('@/views/admin/BranchesView.vue'), meta: { title: 'Chi nhánh', roles: ADMINS } },
      { path: 'users', name: 'users', component: () => import('@/views/admin/UsersView.vue'), meta: { title: 'Người dùng', roles: ADMINS } },
      { path: 'audit', name: 'audit', component: () => import('@/views/admin/AuditView.vue'), meta: { title: 'Nhật ký', roles: ADMINS } },
      { path: 'account', name: 'account', component: () => import('@/views/auth/AccountView.vue'), meta: { title: 'Tài khoản' } },
    ],
  },
  {
    path: '/',
    component: () => import('@/layouts/PublicLayout.vue'),
    children: [
      { path: 'order/:branchCode', name: 'customer-menu', component: () => import('@/views/customer/MenuOrderView.vue'), meta: { title: 'Gọi món' } },
      { path: 'track/:token', name: 'tracking', component: () => import('@/views/customer/TrackingView.vue'), meta: { title: 'Theo dõi đơn' } },
    ],
  },
  { path: '/queue/:branchCode', name: 'queue', component: () => import('@/views/orders/QueueDisplayView.vue'), meta: { title: 'Màn hình gọi số' } },
  { path: '/forbidden', name: 'forbidden', component: () => import('@/views/errors/ForbiddenView.vue'), meta: { title: 'Không có quyền' } },
  { path: '/session-expired', name: 'session-expired', component: () => import('@/views/errors/SessionExpiredView.vue'), meta: { title: 'Hết phiên' } },
  { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/errors/NotFoundView.vue'), meta: { title: 'Không tìm thấy' } },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})

let bootstrapped = false

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (!bootstrapped) {
    bootstrapped = true
    await auth.loadMe()
  }

  if (to.matched.some((r) => r.meta.requiresAuth) && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'login' && auth.isAuthenticated) return { name: 'dashboard' }

  const roles = to.meta.roles
  if (roles && !auth.hasRole(...roles)) return { name: 'forbidden' }
  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · Brewline` : 'Brewline · Quản lý chuỗi F&B'
})
