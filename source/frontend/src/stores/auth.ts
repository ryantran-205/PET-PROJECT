import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { api } from '@/api/http'
import { tokenStore, type AuthPayload } from '@/api/tokenStore'
import type { Me, Role } from '@/api/types'

const BRANCH_KEY = 'brewline.branch'

export const useAuthStore = defineStore('auth', () => {
  const me = ref<Me | null>(null)
  const loading = ref(false)
  const currentBranchId = ref<string | null>(localStorage.getItem(BRANCH_KEY))

  const isAuthenticated = computed(() => !!me.value)
  const role = computed<Role | null>(() => me.value?.role ?? null)
  const isSuperAdmin = computed(() => role.value === 'SuperAdmin')
  const isAdmin = computed(() => role.value === 'SuperAdmin' || role.value === 'BranchAdmin')
  const branches = computed(() => me.value?.branches ?? [])
  const currentBranch = computed(() => branches.value.find((b) => b.id === currentBranchId.value) ?? branches.value[0] ?? null)

  watch(currentBranch, (b) => {
    if (b) localStorage.setItem(BRANCH_KEY, b.id)
  })

  async function login(userName: string, password: string) {
    const data = await api.post<AuthPayload & { user: Me }>('/auth/login', { userName, password }, { anonymous: true })
    tokenStore.save(data)
    me.value = data.user
    if (!branches.value.some((b) => b.id === currentBranchId.value)) currentBranchId.value = branches.value[0]?.id ?? null
  }

  async function loadMe() {
    if (!tokenStore.accessToken && !tokenStore.refreshToken) return false
    loading.value = true
    try {
      me.value = await api.get<Me>('/me')
      return true
    } catch {
      me.value = null
      return false
    } finally {
      loading.value = false
    }
  }

  async function logout() {
    const refreshToken = tokenStore.refreshToken
    tokenStore.clear()
    me.value = null
    if (refreshToken) {
      try {
        await api.post('/auth/logout', { refreshToken }, { anonymous: true })
      } catch {
        /* already invalid */
      }
    }
  }

  function setBranch(id: string) {
    currentBranchId.value = id
  }

  function hasRole(...roles: Role[]) {
    return !!role.value && roles.includes(role.value)
  }

  return { me, loading, isAuthenticated, role, isSuperAdmin, isAdmin, branches, currentBranch, login, loadMe, logout, setBranch, hasRole }
})
