<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, ApiError, errorMessage } from '@/api/http'
import type { Employee, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { money } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const rows = ref<Employee[]>([])
const loading = ref(false)
const search = ref('')
const dialog = ref(false)
const saving = ref(false)
const editing = ref<Employee | null>(null)
const fieldErrors = ref<Record<string, string>>({})
const form = reactive({ employeeCode: '', fullName: '', phone: '', email: '', position: '', hourlyRate: 25000, isActive: true })

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    const res = await api.get<Paged<Employee>>(`/branches/${auth.currentBranch.id}/employees`, { search: search.value, pageSize: 200 })
    rows.value = res.items
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = null
  Object.assign(form, { employeeCode: '', fullName: '', phone: '', email: '', position: '', hourlyRate: 25000, isActive: true })
  fieldErrors.value = {}
  dialog.value = true
}

function openEdit(e: Employee) {
  editing.value = e
  Object.assign(form, { ...e, phone: e.phone ?? '', email: e.email ?? '' })
  fieldErrors.value = {}
  dialog.value = true
}

async function save() {
  if (!auth.currentBranch) return
  saving.value = true
  fieldErrors.value = {}
  const body = { ...form, phone: form.phone || null, email: form.email || null, position: form.position || null }
  try {
    if (editing.value) {
      await api.put(`/employees/${editing.value.id}`, { ...body, userId: editing.value.userId ?? null, rowVersion: editing.value.rowVersion })
    } else {
      await api.post(`/branches/${auth.currentBranch.id}/employees`, { ...body, userId: null })
    }
    toast.add({ severity: 'success', summary: 'Đã lưu nhân viên', life: 2500 })
    dialog.value = false
    await load()
  } catch (e) {
    if (e instanceof ApiError) fieldErrors.value = e.fieldErrors
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Nhân viên</h1>
        <p>Hồ sơ nhân viên và đơn giá giờ tại {{ auth.currentBranch?.name }}.</p>
      </div>
      <div class="toolbar">
        <InputText v-model="search" placeholder="Tìm theo tên / mã" @keyup.enter="load" />
        <Button id="btn-new-employee" label="Thêm nhân viên" icon="pi pi-plus" @click="openCreate" />
      </div>
    </div>
    <div class="card flush">
      <DataTable :value="rows" :loading="loading" paginator :rows="20">
        <template #empty><EmptyState icon="pi-id-card" title="Chưa có nhân viên" /></template>
        <Column field="employeeCode" header="Mã" sortable><template #body="{ data: e }"><span class="mono">{{ e.employeeCode }}</span></template></Column>
        <Column field="fullName" header="Họ tên" sortable />
        <Column field="position" header="Vị trí" />
        <Column header="Liên hệ"><template #body="{ data: e }"><span class="small muted">{{ e.phone ?? e.email ?? '—' }}</span></template></Column>
        <Column header="Đơn giá giờ" class="right"><template #body="{ data: e }">{{ money(e.hourlyRate) }}</template></Column>
        <Column header="Tài khoản"><template #body="{ data: e }"><i v-if="e.userId" class="pi pi-link" style="color: var(--success)" v-tooltip.top="'Đã liên kết tài khoản'" /></template></Column>
        <Column header="Trạng thái"><template #body="{ data: e }"><span class="pill" :class="e.isActive ? 'Ready' : 'Cancelled'">{{ e.isActive ? 'Đang làm' : 'Nghỉ việc' }}</span></template></Column>
        <Column header=""><template #body="{ data: e }"><Button icon="pi pi-pencil" text rounded @click="openEdit(e)" /></template></Column>
      </DataTable>
    </div>

    <Dialog v-model:visible="dialog" modal :header="editing ? 'Sửa nhân viên' : 'Thêm nhân viên'" :style="{ width: '600px' }">
      <form class="form-grid" @submit.prevent="save">
        <div class="field">
          <label for="emp-code">Mã nhân viên</label>
          <InputText id="emp-code" v-model="form.employeeCode" :disabled="!!editing" :invalid="!!fieldErrors.employeeCode" />
          <small class="error">{{ fieldErrors.employeeCode }}</small>
        </div>
        <div class="field">
          <label for="emp-name">Họ tên</label>
          <InputText id="emp-name" v-model="form.fullName" :invalid="!!fieldErrors.fullName" />
          <small class="error">{{ fieldErrors.fullName }}</small>
        </div>
        <div class="field"><label for="emp-phone">Điện thoại</label><InputText id="emp-phone" v-model="form.phone" /></div>
        <div class="field"><label for="emp-email">Email</label><InputText id="emp-email" v-model="form.email" /></div>
        <div class="field"><label for="emp-pos">Vị trí</label><InputText id="emp-pos" v-model="form.position" placeholder="Barista, Thu ngân…" /></div>
        <div class="field">
          <label for="emp-rate">Đơn giá giờ (VND)</label>
          <InputNumber input-id="emp-rate" v-model="form.hourlyRate" :min="0" :step="1000" locale="vi-VN" :invalid="!!fieldErrors.hourlyRate" />
          <small class="error">{{ fieldErrors.hourlyRate }}</small>
        </div>
        <div v-if="editing" class="row"><ToggleSwitch v-model="form.isActive" input-id="emp-active" /><label for="emp-active">Đang làm việc</label></div>
      </form>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="dialog = false" />
        <Button id="btn-save-employee" label="Lưu" icon="pi pi-check" :loading="saving" @click="save" />
      </template>
    </Dialog>
  </div>
</template>
