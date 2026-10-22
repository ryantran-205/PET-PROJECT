<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, ApiError, errorMessage } from '@/api/http'
import type { Paged, Role, User } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime, roleLabels } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const rows = ref<User[]>([])
const total = ref(0)
const page = ref(1)
const search = ref('')
const loading = ref(false)
const dialog = ref(false)
const saving = ref(false)
const editing = ref<User | null>(null)
const fieldErrors = ref<Record<string, string>>({})
const form = reactive({ userName: '', displayName: '', email: '', password: '', role: 'BranchStaff' as Role, isActive: true, branchIds: [] as string[] })
const resetDialog = ref(false)
const resetTarget = ref<User | null>(null)
const resetPassword = ref('')

const roleOptions = computed(() =>
  (auth.isSuperAdmin ? ['SuperAdmin', 'BranchAdmin', 'BranchStaff'] : ['BranchStaff']).map((r) => ({ label: roleLabels[r], value: r })),
)
const branchOptions = computed(() => auth.branches.map((b) => ({ label: `${b.code} · ${b.name}`, value: b.id })))
const branchName = (id: string) => auth.branches.find((b) => b.id === id)?.code ?? '…'

async function load() {
  loading.value = true
  try {
    const res = await api.get<Paged<User>>('/users', { search: search.value, page: page.value, pageSize: 20 })
    rows.value = res.items
    total.value = res.totalCount
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = null
  Object.assign(form, { userName: '', displayName: '', email: '', password: '', role: 'BranchStaff', isActive: true, branchIds: auth.currentBranch ? [auth.currentBranch.id] : [] })
  fieldErrors.value = {}
  dialog.value = true
}

function openEdit(u: User) {
  editing.value = u
  Object.assign(form, { userName: u.userName, displayName: u.displayName, email: u.email ?? '', password: '', role: u.role, isActive: u.isActive, branchIds: [...u.branchIds] })
  fieldErrors.value = {}
  dialog.value = true
}

async function save() {
  saving.value = true
  fieldErrors.value = {}
  try {
    if (editing.value) {
      await api.put(`/users/${editing.value.id}`, { displayName: form.displayName, email: form.email || null, role: form.role, isActive: form.isActive, branchIds: form.branchIds })
    } else {
      await api.post('/users', { ...form, email: form.email || null })
    }
    toast.add({ severity: 'success', summary: 'Đã lưu người dùng', life: 2500 })
    dialog.value = false
    await load()
  } catch (e) {
    if (e instanceof ApiError) fieldErrors.value = e.fieldErrors
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  } finally {
    saving.value = false
  }
}

function openReset(u: User) {
  resetTarget.value = u
  resetPassword.value = ''
  resetDialog.value = true
}

async function doReset() {
  if (!resetTarget.value) return
  try {
    await api.post(`/users/${resetTarget.value.id}/reset-password`, { newPassword: resetPassword.value })
    toast.add({ severity: 'success', summary: 'Đã đặt lại mật khẩu', detail: 'Các phiên đăng nhập cũ đã bị thu hồi.', life: 3500 })
    resetDialog.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

function onPage(e: { page: number }) {
  page.value = e.page + 1
  load()
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Người dùng</h1>
        <p>Tài khoản đăng nhập, vai trò và phạm vi chi nhánh.</p>
      </div>
      <div class="toolbar">
        <InputText v-model="search" placeholder="Tìm theo tên / username" @keyup.enter="(page = 1), load()" />
        <Button id="btn-new-user" label="Thêm người dùng" icon="pi pi-user-plus" @click="openCreate" />
      </div>
    </div>

    <div class="card flush">
      <DataTable :value="rows" :loading="loading" lazy paginator :rows="20" :total-records="total" @page="onPage">
        <template #empty><EmptyState icon="pi-users" title="Không có người dùng" /></template>
        <Column header="Người dùng">
          <template #body="{ data: u }">
            <div style="font-weight: 600">{{ u.displayName }}</div>
            <div class="faint small mono">{{ u.userName }}</div>
          </template>
        </Column>
        <Column header="Vai trò"><template #body="{ data: u }">{{ roleLabels[u.role] }}</template></Column>
        <Column header="Chi nhánh">
          <template #body="{ data: u }">
            <span v-if="u.role === 'SuperAdmin'" class="muted small">Toàn chuỗi</span>
            <div v-else class="row wrap" style="gap: 6px"><span v-for="id in u.branchIds" :key="id" class="pill Unpaid">{{ branchName(id) }}</span></div>
          </template>
        </Column>
        <Column header="Trạng thái">
          <template #body="{ data: u }"><span class="pill" :class="u.isActive ? 'Ready' : 'Cancelled'">{{ u.isActive ? 'Hoạt động' : 'Đã khóa' }}</span></template>
        </Column>
        <Column header="Đăng nhập gần nhất"><template #body="{ data: u }"><span class="small">{{ dateTime(u.lastLoginAtUtc) }}</span></template></Column>
        <Column header="" style="width: 110px">
          <template #body="{ data: u }">
            <Button icon="pi pi-pencil" text rounded v-tooltip.top="'Sửa'" @click="openEdit(u)" />
            <Button icon="pi pi-key" text rounded severity="warn" v-tooltip.top="'Đặt lại mật khẩu'" @click="openReset(u)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <Dialog v-model:visible="dialog" modal :header="editing ? 'Sửa người dùng' : 'Thêm người dùng'" :style="{ width: '600px' }">
      <form class="form-grid" @submit.prevent="save">
        <div class="field">
          <label for="u-username">Tên đăng nhập</label>
          <InputText id="u-username" v-model="form.userName" :disabled="!!editing" :invalid="!!fieldErrors.userName" autocomplete="off" />
          <small class="error">{{ fieldErrors.userName }}</small>
        </div>
        <div class="field">
          <label for="u-display">Họ tên</label>
          <InputText id="u-display" v-model="form.displayName" :invalid="!!fieldErrors.displayName" />
          <small class="error">{{ fieldErrors.displayName }}</small>
        </div>
        <div class="field"><label for="u-email">Email</label><InputText id="u-email" v-model="form.email" type="email" /></div>
        <div class="field">
          <label for="u-role">Vai trò</label>
          <Select input-id="u-role" v-model="form.role" :options="roleOptions" option-label="label" option-value="value" />
        </div>
        <div v-if="!editing" class="field full">
          <label for="u-password">Mật khẩu ban đầu</label>
          <Password input-id="u-password" v-model="form.password" toggle-mask fluid autocomplete="new-password" :invalid="!!fieldErrors.password" />
          <small class="error">{{ fieldErrors.password }}</small>
        </div>
        <div v-if="form.role !== 'SuperAdmin'" class="field full">
          <label for="u-branches">Chi nhánh được truy cập</label>
          <MultiSelect input-id="u-branches" v-model="form.branchIds" :options="branchOptions" option-label="label" option-value="value" display="chip" :invalid="!!fieldErrors.branchIds" />
          <small class="error">{{ fieldErrors.branchIds }}</small>
        </div>
        <div v-if="editing" class="row"><ToggleSwitch v-model="form.isActive" input-id="u-active" /><label for="u-active">Đang hoạt động</label></div>
      </form>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="dialog = false" />
        <Button id="btn-save-user" label="Lưu" icon="pi pi-check" :loading="saving" @click="save" />
      </template>
    </Dialog>

    <Dialog v-model:visible="resetDialog" modal header="Đặt lại mật khẩu" :style="{ width: '420px' }">
      <p class="muted">Đặt mật khẩu mới cho <strong>{{ resetTarget?.displayName }}</strong>. Mọi phiên đăng nhập hiện tại sẽ bị thu hồi.</p>
      <Password v-model="resetPassword" toggle-mask fluid autocomplete="new-password" />
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="resetDialog = false" />
        <Button label="Đặt lại" icon="pi pi-key" severity="warn" :disabled="resetPassword.length < 8" @click="doReset" />
      </template>
    </Dialog>
  </div>
</template>
