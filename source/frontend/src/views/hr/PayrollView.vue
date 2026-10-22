<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { api, errorMessage } from '@/api/http'
import type { PayrollPeriod, PayrollSummary } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime, minutesToHours, money, qty } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const confirm = useConfirm()
const now = new Date()
const pad = (n: number) => String(n).padStart(2, '0')
const from = ref(`${now.getFullYear()}-${pad(now.getMonth() + 1)}-01`)
const to = ref(`${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate())}`)
const summary = ref<PayrollSummary | null>(null)
const periods = ref<PayrollPeriod[]>([])
const loading = ref(false)

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    const [s, p] = await Promise.all([
      api.get<PayrollSummary>('/reports/payroll-summary', { branchId: auth.currentBranch.id, from: from.value, to: to.value }),
      api.get<PayrollPeriod[]>(`/branches/${auth.currentBranch.id}/payroll-periods`),
    ])
    summary.value = s
    periods.value = p
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function closePeriod() {
  confirm.require({
    header: 'Chốt kỳ công',
    message: `Chốt kỳ ${from.value} → ${to.value}? Chấm công trong kỳ sẽ bị khóa, không thể điều chỉnh.`,
    acceptLabel: 'Chốt kỳ',
    rejectLabel: 'Hủy',
    accept: async () => {
      try {
        await api.post(`/branches/${auth.currentBranch!.id}/payroll-periods`, { from: from.value, to: to.value })
        toast.add({ severity: 'success', summary: 'Đã chốt kỳ công', life: 3000 })
        load()
      } catch (e) {
        toast.add({ severity: 'error', summary: 'Không chốt được', detail: errorMessage(e), life: 6000 })
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
        <h1>Bảng công &amp; lương cơ bản</h1>
        <p>Tổng hợp giờ công theo kỳ. Đây là bảng lương cơ bản, chưa gồm thuế TNCN/BHXH.</p>
      </div>
      <div class="toolbar">
        <InputText v-model="from" type="date" aria-label="Từ ngày" />
        <InputText v-model="to" type="date" aria-label="Đến ngày" />
        <Button icon="pi pi-search" label="Xem" @click="load" />
        <Button v-if="auth.isAdmin" id="btn-close-period" label="Chốt kỳ" icon="pi pi-lock" severity="warn" outlined :disabled="!!summary?.closedPeriodId" @click="closePeriod" />
      </div>
    </div>

    <div class="grid grid-3" style="margin-bottom: 18px">
      <div class="stat"><div class="label">Tổng lương kỳ</div><div class="value">{{ money(summary?.totalAmount) }}</div><div class="hint">{{ summary?.lines.length ?? 0 }} nhân viên</div></div>
      <div class="stat"><div class="label">Ca chưa check-out</div><div class="value" :style="{ color: summary?.openRecords ? 'var(--warning)' : undefined }">{{ summary?.openRecords ?? 0 }}</div><div class="hint">Cần xử lý trước khi chốt</div></div>
      <div class="stat"><div class="label">Trạng thái kỳ</div><div class="value" style="font-size: 1.2rem">{{ summary?.closedPeriodId ? 'Đã chốt' : 'Đang mở' }}</div><div class="hint">{{ summary?.formula }}</div></div>
    </div>

    <div class="card flush" style="margin-bottom: 18px">
      <DataTable :value="summary?.lines ?? []" :loading="loading">
        <template #empty><EmptyState icon="pi-wallet" title="Không có giờ công trong kỳ" /></template>
        <Column header="Nhân viên"><template #body="{ data: l }"><strong>{{ l.employeeName }}</strong> <span class="faint mono small">{{ l.employeeCode }}</span></template></Column>
        <Column field="shiftCount" header="Số ca" class="right" />
        <Column header="Thực tế" class="right"><template #body="{ data: l }">{{ minutesToHours(l.workedMinutes) }}</template></Column>
        <Column header="Tính công" class="right"><template #body="{ data: l }">{{ minutesToHours(l.payableMinutes) }}</template></Column>
        <Column header="Giờ quy đổi" class="right"><template #body="{ data: l }">{{ qty(l.weightedHours) }}</template></Column>
        <Column header="Đơn giá" class="right"><template #body="{ data: l }">{{ money(l.hourlyRate) }}</template></Column>
        <Column header="Thành tiền" class="right"><template #body="{ data: l }"><MoneyText :value="l.amount" strong /></template></Column>
      </DataTable>
    </div>

    <div class="card">
      <div class="card-title"><h2>Kỳ đã chốt</h2></div>
      <EmptyState v-if="!periods.length" icon="pi-lock" title="Chưa chốt kỳ nào" />
      <DataTable v-else :value="periods" size="small">
        <Column header="Kỳ"><template #body="{ data: p }">{{ p.periodStart }} → {{ p.periodEnd }}</template></Column>
        <Column header="Chốt lúc"><template #body="{ data: p }">{{ dateTime(p.closedAtUtc) }}</template></Column>
        <Column field="lineCount" header="Số NV" class="right" />
        <Column header="Tổng" class="right"><template #body="{ data: p }"><MoneyText :value="p.totalAmount" strong /></template></Column>
      </DataTable>
    </div>
  </div>
</template>
