<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { Employee, LeaveRequest, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime, todayLocal } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const rows = ref<LeaveRequest[]>([])
const loading = ref(false)
const status = ref<string | null>('Pending')
const statusOptions = [
  { label: 'Chờ duyệt', value: 'Pending' },
  { label: 'Đã duyệt', value: 'Approved' },
  { label: 'Từ chối', value: 'Rejected' },
  { label: 'Tất cả', value: null },
]
const employees = ref<Employee[]>([])
const dialog = ref(false)
const form = reactive({ employeeId: null as string | null, startDate: todayLocal(1), endDate: todayLocal(1), reason: '' })
const review = reactive({ open: false, target: null as LeaveRequest | null, approve: true, note: '' })

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    rows.value = (await api.get<Paged<LeaveRequest>>('/leave-requests', { branchId: auth.currentBranch.id, status: status.value, pageSize: 100 })).items
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function submit() {
  try {
    await api.post('/leave-requests', form)
    dialog.value = false
    toast.add({ severity: 'success', summary: 'Đã gửi đơn nghỉ phép', life: 2500 })
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không gửi được', detail: errorMessage(e), life: 5000 })
  }
}

function openReview(r: LeaveRequest, approve: boolean) {
  Object.assign(review, { open: true, target: r, approve, note: '' })
}

async function doReview() {
  if (!review.target) return
  try {
    await api.post(`/leave-requests/${review.target.id}/${review.approve ? 'approve' : 'reject'}`, { note: review.note || null })
    review.open = false
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

onMounted(async () => {
  await load()
  if (auth.isAdmin && auth.currentBranch) {
    employees.value = (await api.get<Paged<Employee>>(`/branches/${auth.currentBranch.id}/employees`, { isActive: true, pageSize: 200 })).items
  }
})
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Nghỉ phép</h1>
        <p>Gửi và duyệt đơn nghỉ phép.</p>
      </div>
      <div class="toolbar">
        <SelectButton v-model="status" :options="statusOptions" option-label="label" option-value="value" @change="load" />
        <Button id="btn-new-leave" label="Tạo đơn" icon="pi pi-plus" :disabled="!auth.me?.employeeId && !auth.isAdmin" @click="dialog = true" />
      </div>
    </div>
    <div class="card flush">
      <DataTable :value="rows" :loading="loading">
        <template #empty><EmptyState icon="pi-calendar-minus" title="Không có đơn nghỉ phép" /></template>
        <Column field="employeeName" header="Nhân viên" />
        <Column header="Thời gian"><template #body="{ data: r }">{{ r.startDate }} → {{ r.endDate }}</template></Column>
        <Column field="reason" header="Lý do" />
        <Column header="Trạng thái"><template #body="{ data: r }"><StatusPill :status="r.status" /></template></Column>
        <Column header="Ghi chú duyệt"><template #body="{ data: r }"><span class="small muted">{{ r.reviewNote ?? '—' }}</span></template></Column>
        <Column header="Gửi lúc"><template #body="{ data: r }"><span class="small">{{ dateTime(r.createdAtUtc) }}</span></template></Column>
        <Column v-if="auth.isAdmin" header="">
          <template #body="{ data: r }">
            <template v-if="r.status === 'Pending'">
              <Button icon="pi pi-check" text rounded severity="success" v-tooltip.top="'Duyệt'" @click="openReview(r, true)" />
              <Button icon="pi pi-times" text rounded severity="danger" v-tooltip.top="'Từ chối'" @click="openReview(r, false)" />
            </template>
          </template>
        </Column>
      </DataTable>
    </div>

    <Dialog v-model:visible="dialog" modal header="Tạo đơn nghỉ phép" :style="{ width: '460px' }">
      <div class="stack">
        <div v-if="auth.isAdmin" class="field">
          <label for="lv-emp">Nhân viên (để trống = chính tôi)</label>
          <Select input-id="lv-emp" v-model="form.employeeId" :options="employees" option-label="fullName" option-value="id" show-clear filter />
        </div>
        <div class="form-grid">
          <div class="field"><label for="lv-from">Từ ngày</label><InputText id="lv-from" v-model="form.startDate" type="date" /></div>
          <div class="field"><label for="lv-to">Đến ngày</label><InputText id="lv-to" v-model="form.endDate" type="date" /></div>
        </div>
        <div class="field"><label for="lv-reason">Lý do</label><Textarea id="lv-reason" v-model="form.reason" rows="3" /></div>
      </div>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="dialog = false" />
        <Button label="Gửi đơn" icon="pi pi-send" :disabled="!form.reason.trim()" @click="submit" />
      </template>
    </Dialog>

    <Dialog v-model:visible="review.open" modal :header="review.approve ? 'Duyệt đơn' : 'Từ chối đơn'" :style="{ width: '420px' }">
      <p class="muted">{{ review.target?.employeeName }} · {{ review.target?.startDate }} → {{ review.target?.endDate }}</p>
      <Textarea v-model="review.note" rows="2" placeholder="Ghi chú (tùy chọn)" class="w-full" />
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="review.open = false" />
        <Button :label="review.approve ? 'Duyệt' : 'Từ chối'" :severity="review.approve ? 'success' : 'danger'" @click="doReview" />
      </template>
    </Dialog>
  </div>
</template>
