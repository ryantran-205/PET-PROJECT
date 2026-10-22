<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { Attendance, Employee, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime, elapsed, minutesToHours, timeOnly, todayLocal } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const current = ref<Attendance | null>(null)
const rows = ref<Attendance[]>([])
const total = ref(0)
const page = ref(1)
const from = ref(todayLocal(-6))
const to = ref(todayLocal())
const loading = ref(false)
const acting = ref(false)
const now = ref(Date.now())
const timer = setInterval(() => (now.value = Date.now()), 30000)
onBeforeUnmount(() => clearInterval(timer))

const employees = ref<Employee[]>([])
const proxyEmployee = ref<string | null>(null)
const adjustDialog = ref(false)
const adjustTarget = ref<Attendance | null>(null)
const adjust = reactive({ checkIn: '', checkOut: '', reason: '' })

const hasEmployee = computed(() => !!auth.me?.employeeId)

async function loadCurrent() {
  if (!hasEmployee.value) return
  try {
    current.value = (await api.get<Attendance | undefined>('/attendance/current')) ?? null
  } catch {
    current.value = null
  }
}

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    const res = await api.get<Paged<Attendance>>('/attendance', { branchId: auth.currentBranch.id, from: from.value, to: to.value, page: page.value, pageSize: 50 })
    rows.value = res.items
    total.value = res.totalCount
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function punch(kind: 'check-in' | 'check-out', employeeId?: string | null) {
  acting.value = true
  try {
    await api.post(`/attendance/${kind}`, { employeeId: employeeId ?? null, source: employeeId ? 'Manager' : 'Web' })
    toast.add({ severity: 'success', summary: kind === 'check-in' ? 'Đã check-in' : 'Đã check-out', life: 2500 })
    await Promise.all([loadCurrent(), load()])
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không thực hiện được', detail: errorMessage(e), life: 5000 })
  } finally {
    acting.value = false
  }
}

function toLocalInput(iso?: string | null) {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function openAdjust(a: Attendance) {
  adjustTarget.value = a
  Object.assign(adjust, { checkIn: toLocalInput(a.checkInUtc), checkOut: toLocalInput(a.checkOutUtc), reason: '' })
  adjustDialog.value = true
}

async function saveAdjust() {
  if (!adjustTarget.value) return
  try {
    await api.put(`/attendance/${adjustTarget.value.id}`, {
      checkInUtc: new Date(adjust.checkIn).toISOString(),
      checkOutUtc: adjust.checkOut ? new Date(adjust.checkOut).toISOString() : null,
      reason: adjust.reason,
    })
    adjustDialog.value = false
    toast.add({ severity: 'success', summary: 'Đã điều chỉnh (có ghi audit)', life: 2500 })
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không điều chỉnh được', detail: errorMessage(e), life: 5000 })
  }
}

onMounted(async () => {
  await Promise.all([loadCurrent(), load()])
  if (auth.isAdmin && auth.currentBranch) {
    employees.value = (await api.get<Paged<Employee>>(`/branches/${auth.currentBranch.id}/employees`, { isActive: true, pageSize: 200 })).items
  }
})
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Chấm công</h1>
        <p>Check-in/out theo giờ máy chủ; giờ công tính theo ca đã xếp và quy tắc làm tròn.</p>
      </div>
    </div>

    <div class="grid grid-2" style="margin-bottom: 18px">
      <div class="card punch-card">
        <template v-if="hasEmployee">
          <div class="punch-status" :class="{ on: !!current }">
            <i class="pi" :class="current ? 'pi-play-circle' : 'pi-pause-circle'" />
            <div>
              <div style="font-weight: 700; font-size: 1.1rem">{{ current ? 'Đang trong ca' : 'Chưa check-in' }}</div>
              <div v-if="current" class="muted small">Từ {{ timeOnly(current.checkInUtc) }} · đã làm {{ elapsed(current.checkInUtc, now) }}</div>
              <div v-else class="muted small">Nhấn để bắt đầu ca làm việc của bạn.</div>
            </div>
          </div>
          <Button v-if="!current" id="btn-check-in" label="Check-in" icon="pi pi-sign-in" size="large" :loading="acting" class="w-full pulse" @click="punch('check-in')" />
          <Button v-else id="btn-check-out" label="Check-out" icon="pi pi-sign-out" size="large" severity="warn" :loading="acting" class="w-full" @click="punch('check-out')" />
        </template>
        <EmptyState v-else icon="pi-user" title="Tài khoản chưa liên kết hồ sơ nhân viên" message="Liên hệ quản lý để được liên kết trước khi tự chấm công." />
      </div>
      <div v-if="auth.isAdmin" class="card">
        <div class="card-title"><h2>Chấm công hộ</h2><span class="faint small">ghi nguồn Manager</span></div>
        <div class="stack">
          <Select v-model="proxyEmployee" :options="employees" option-label="fullName" option-value="id" filter placeholder="Chọn nhân viên" />
          <div class="row">
            <Button label="Check-in" icon="pi pi-sign-in" outlined :disabled="!proxyEmployee" @click="punch('check-in', proxyEmployee)" />
            <Button label="Check-out" icon="pi pi-sign-out" outlined severity="warn" :disabled="!proxyEmployee" @click="punch('check-out', proxyEmployee)" />
          </div>
        </div>
      </div>
    </div>

    <div class="card flush">
      <div class="row wrap" style="padding: 14px 16px">
        <h2>Lịch sử</h2>
        <div class="spacer" />
        <InputText v-model="from" type="date" aria-label="Từ ngày" />
        <InputText v-model="to" type="date" aria-label="Đến ngày" />
        <Button icon="pi pi-search" @click="(page = 1), load()" />
      </div>
      <DataTable :value="rows" :loading="loading" lazy paginator :rows="50" :total-records="total" size="small" @page="(e) => ((page = e.page + 1), load())">
        <template #empty><EmptyState icon="pi-clock" title="Không có bản ghi chấm công" /></template>
        <Column field="employeeName" header="Nhân viên" />
        <Column field="workDate" header="Ngày công" />
        <Column header="Vào"><template #body="{ data: a }">{{ dateTime(a.checkInUtc) }}</template></Column>
        <Column header="Ra"><template #body="{ data: a }"><span :class="{ muted: !a.checkOutUtc }">{{ a.checkOutUtc ? dateTime(a.checkOutUtc) : 'Đang làm' }}</span></template></Column>
        <Column header="Thực tế" class="right"><template #body="{ data: a }">{{ minutesToHours(a.workedMinutes) }}</template></Column>
        <Column header="Tính công" class="right"><template #body="{ data: a }"><strong>{{ minutesToHours(a.payableMinutes) }}</strong></template></Column>
        <Column header="Hệ số" class="right"><template #body="{ data: a }">×{{ a.payMultiplier }}</template></Column>
        <Column header="">
          <template #body="{ data: a }">
            <i v-if="a.isLocked" class="pi pi-lock faint" v-tooltip.top="'Đã chốt kỳ công'" />
            <Button v-else-if="auth.isAdmin" icon="pi pi-pencil" text rounded v-tooltip.top="'Điều chỉnh'" @click="openAdjust(a)" />
          </template>
        </Column>
      </DataTable>
    </div>

    <Dialog v-model:visible="adjustDialog" modal header="Điều chỉnh chấm công" :style="{ width: '460px' }">
      <div class="stack">
        <div class="field"><label for="adj-in">Giờ vào</label><InputText id="adj-in" v-model="adjust.checkIn" type="datetime-local" /></div>
        <div class="field"><label for="adj-out">Giờ ra</label><InputText id="adj-out" v-model="adjust.checkOut" type="datetime-local" /></div>
        <div class="field"><label for="adj-reason">Lý do (bắt buộc)</label><Textarea id="adj-reason" v-model="adjust.reason" rows="2" /></div>
      </div>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="adjustDialog = false" />
        <Button label="Lưu điều chỉnh" icon="pi pi-check" :disabled="adjust.reason.trim().length < 3" @click="saveAdjust" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.punch-card { display: flex; flex-direction: column; gap: 18px; justify-content: center; }
.punch-status { display: flex; gap: 14px; align-items: center; }
.punch-status .pi { font-size: 2.4rem; color: var(--text-faint); }
.punch-status.on .pi { color: var(--success); }
</style>
