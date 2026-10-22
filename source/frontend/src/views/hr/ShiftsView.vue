<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { api, errorMessage } from '@/api/http'
import type { Employee, Paged, ShiftAssignment, WorkShift } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { todayLocal } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const confirm = useConfirm()
const shifts = ref<WorkShift[]>([])
const assignments = ref<ShiftAssignment[]>([])
const employees = ref<Employee[]>([])
const weekStart = ref(mondayOf(todayLocal()))
const loading = ref(false)

const shiftDialog = ref(false)
const editingShift = ref<WorkShift | null>(null)
const shiftForm = reactive({ name: '', startTime: '07:00', endTime: '15:00', payMultiplier: 1, effectiveFrom: todayLocal(), effectiveTo: '', isActive: true })

const assignDialog = ref(false)
const assignForm = reactive({ employeeId: '', workShiftId: '', workDate: todayLocal(), note: '' })

function mondayOf(iso: string) {
  const d = new Date(iso + 'T00:00:00')
  const diff = (d.getDay() + 6) % 7
  d.setDate(d.getDate() - diff)
  return isoOf(d)
}
function isoOf(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
function addDays(iso: string, n: number) {
  const d = new Date(iso + 'T00:00:00')
  d.setDate(d.getDate() + n)
  return isoOf(d)
}

const days = computed(() => Array.from({ length: 7 }, (_, i) => addDays(weekStart.value, i)))
const dayNames = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN']
const byDay = (day: string) => assignments.value.filter((a) => a.workDate === day)
const activeShifts = computed(() => shifts.value.filter((s) => s.isActive))
const hhmm = (t: string) => t.slice(0, 5)

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  const id = auth.currentBranch.id
  try {
    const [s, a] = await Promise.all([
      api.get<WorkShift[]>(`/branches/${id}/shifts`),
      api.get<ShiftAssignment[]>(`/branches/${id}/shift-assignments`, { from: days.value[0], to: days.value[6] }),
    ])
    shifts.value = s
    assignments.value = a
    if (auth.isAdmin && !employees.value.length) {
      employees.value = (await api.get<Paged<Employee>>(`/branches/${id}/employees`, { isActive: true, pageSize: 200 })).items
    }
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function shiftWeek(n: number) {
  weekStart.value = addDays(weekStart.value, 7 * n)
  load()
}

function openShift(s?: WorkShift) {
  editingShift.value = s ?? null
  Object.assign(
    shiftForm,
    s
      ? { name: s.name, startTime: hhmm(s.startTime), endTime: hhmm(s.endTime), payMultiplier: s.payMultiplier, effectiveFrom: s.effectiveFrom, effectiveTo: s.effectiveTo ?? '', isActive: s.isActive }
      : { name: '', startTime: '07:00', endTime: '15:00', payMultiplier: 1, effectiveFrom: todayLocal(), effectiveTo: '', isActive: true },
  )
  shiftDialog.value = true
}

async function saveShift() {
  if (!auth.currentBranch) return
  const body = { ...shiftForm, startTime: shiftForm.startTime + ':00', endTime: shiftForm.endTime + ':00', effectiveTo: shiftForm.effectiveTo || null }
  try {
    if (editingShift.value) await api.put(`/branches/${auth.currentBranch.id}/shifts/${editingShift.value.id}`, body)
    else await api.post(`/branches/${auth.currentBranch.id}/shifts`, body)
    shiftDialog.value = false
    toast.add({ severity: 'success', summary: 'Đã lưu ca', life: 2000 })
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  }
}

function openAssign(day: string) {
  Object.assign(assignForm, { employeeId: '', workShiftId: activeShifts.value[0]?.id ?? '', workDate: day, note: '' })
  assignDialog.value = true
}

async function saveAssign() {
  if (!auth.currentBranch) return
  try {
    await api.post(`/branches/${auth.currentBranch.id}/shift-assignments`, { ...assignForm, note: assignForm.note || null })
    assignDialog.value = false
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không xếp được ca', detail: errorMessage(e), life: 5000 })
  }
}

function removeAssign(a: ShiftAssignment) {
  confirm.require({
    header: 'Xóa phân ca',
    message: `Xóa ca ${a.shiftName} của ${a.employeeName} ngày ${a.workDate}?`,
    acceptLabel: 'Xóa',
    rejectLabel: 'Hủy',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await api.del(`/branches/${auth.currentBranch!.id}/shift-assignments/${a.id}`)
        load()
      } catch (e) {
        toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
      }
    },
  })
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Ca làm &amp; phân ca</h1>
        <p>Định nghĩa ca (hỗ trợ ca qua đêm) và lịch làm việc theo tuần.</p>
      </div>
      <div class="toolbar">
        <Button icon="pi pi-chevron-left" text rounded aria-label="Tuần trước" @click="shiftWeek(-1)" />
        <span class="mono">{{ days[0] }} → {{ days[6] }}</span>
        <Button icon="pi pi-chevron-right" text rounded aria-label="Tuần sau" @click="shiftWeek(1)" />
        <Button v-if="auth.isAdmin" id="btn-new-shift" label="Thêm ca" icon="pi pi-plus" outlined @click="openShift()" />
      </div>
    </div>

    <div class="row wrap" style="margin-bottom: 16px">
      <div v-for="s in shifts" :key="s.id" class="shift-chip" :class="{ off: !s.isActive }" @click="auth.isAdmin && openShift(s)">
        <strong>{{ s.name }}</strong>
        <span class="mono small">{{ hhmm(s.startTime) }}–{{ hhmm(s.endTime) }}</span>
        <span v-if="s.crossesMidnight" class="pill Preparing">qua đêm</span>
        <span v-if="s.payMultiplier !== 1" class="pill Refunded">×{{ s.payMultiplier }}</span>
      </div>
    </div>

    <div class="week">
      <div v-for="(d, i) in days" :key="d" class="day card" :class="{ today: d === todayLocal() }">
        <div class="day-head">
          <span>{{ dayNames[i] }}</span>
          <span class="faint small">{{ d.slice(8, 10) }}/{{ d.slice(5, 7) }}</span>
          <div class="spacer" />
          <Button v-if="auth.isAdmin" icon="pi pi-plus" text rounded size="small" aria-label="Xếp ca" @click="openAssign(d)" />
        </div>
        <Skeleton v-if="loading" height="60px" />
        <div v-for="a in byDay(d)" :key="a.id" class="assign">
          <div style="font-weight: 600">{{ a.employeeName }}</div>
          <div class="small muted">{{ a.shiftName }} · {{ hhmm(a.startTime) }}–{{ hhmm(a.endTime) }}</div>
          <Button v-if="auth.isAdmin" icon="pi pi-times" text rounded size="small" severity="danger" class="rm" aria-label="Xóa" @click="removeAssign(a)" />
        </div>
        <div v-if="!loading && !byDay(d).length" class="faint small" style="padding: 8px 0">Chưa xếp ca</div>
      </div>
    </div>

    <Dialog v-model:visible="shiftDialog" modal :header="editingShift ? 'Sửa ca' : 'Thêm ca'" :style="{ width: '520px' }">
      <form class="form-grid" @submit.prevent="saveShift">
        <div class="field full"><label for="sh-name">Tên ca</label><InputText id="sh-name" v-model="shiftForm.name" /></div>
        <div class="field"><label for="sh-start">Bắt đầu</label><InputText id="sh-start" v-model="shiftForm.startTime" type="time" /></div>
        <div class="field"><label for="sh-end">Kết thúc</label><InputText id="sh-end" v-model="shiftForm.endTime" type="time" /></div>
        <div class="field"><label for="sh-mult">Hệ số lương</label><InputNumber input-id="sh-mult" v-model="shiftForm.payMultiplier" :min="0.5" :max="5" :step="0.1" :max-fraction-digits="2" show-buttons /></div>
        <div class="field"><label for="sh-from">Hiệu lực từ</label><InputText id="sh-from" v-model="shiftForm.effectiveFrom" type="date" /></div>
        <div class="field"><label for="sh-to">Hiệu lực đến (tùy chọn)</label><InputText id="sh-to" v-model="shiftForm.effectiveTo" type="date" /></div>
        <div class="row"><ToggleSwitch v-model="shiftForm.isActive" input-id="sh-active" /><label for="sh-active">Đang áp dụng</label></div>
        <p class="full faint small">Giờ kết thúc nhỏ hơn giờ bắt đầu nghĩa là ca qua đêm (ví dụ 22:00–06:00).</p>
      </form>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="shiftDialog = false" />
        <Button label="Lưu" icon="pi pi-check" @click="saveShift" />
      </template>
    </Dialog>

    <Dialog v-model:visible="assignDialog" modal header="Xếp ca" :style="{ width: '460px' }">
      <div class="stack">
        <div class="field"><label>Ngày</label><InputText v-model="assignForm.workDate" type="date" /></div>
        <div class="field">
          <label for="as-emp">Nhân viên</label>
          <Select input-id="as-emp" v-model="assignForm.employeeId" :options="employees" option-label="fullName" option-value="id" filter placeholder="Chọn nhân viên" />
        </div>
        <div class="field">
          <label for="as-shift">Ca</label>
          <Select input-id="as-shift" v-model="assignForm.workShiftId" :options="activeShifts" option-label="name" option-value="id" />
        </div>
        <div class="field"><label for="as-note">Ghi chú</label><InputText id="as-note" v-model="assignForm.note" /></div>
      </div>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="assignDialog = false" />
        <Button label="Xếp ca" icon="pi pi-check" :disabled="!assignForm.employeeId || !assignForm.workShiftId" @click="saveAssign" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.week { display: grid; grid-template-columns: repeat(7, minmax(150px, 1fr)); gap: 12px; overflow-x: auto; }
.day { padding: 12px; min-height: 180px; }
.day.today { border-color: rgba(255, 179, 92, .45); box-shadow: var(--shadow-glow); }
.day-head { display: flex; align-items: center; gap: 8px; font-weight: 700; margin-bottom: 8px; }
.assign { position: relative; padding: 8px 10px; border-radius: 10px; background: var(--accent-soft); border: 1px solid rgba(255, 179, 92, .18); margin-bottom: 8px; }
.assign .rm { position: absolute; top: 2px; right: 2px; opacity: 0; transition: opacity .15s; }
.assign:hover .rm { opacity: 1; }
.shift-chip { display: flex; gap: 10px; align-items: center; padding: 8px 14px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); cursor: pointer; transition: border-color .15s; }
.shift-chip:hover { border-color: var(--border-strong); }
.shift-chip.off { opacity: .5; }
</style>
