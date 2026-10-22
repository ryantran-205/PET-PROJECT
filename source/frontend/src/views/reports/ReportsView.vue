<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { RevenueReport } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { methodLabels, money, todayLocal } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const from = ref(todayLocal(-29))
const to = ref(todayLocal())
const branchId = ref<string | null>(auth.isSuperAdmin ? null : auth.currentBranch?.id ?? null)
const report = ref<RevenueReport | null>(null)
const loading = ref(false)
const exporting = ref<string | null>(null)
const branchOptions = computed(() => [
  ...(auth.isSuperAdmin || auth.branches.length > 1 ? [{ label: 'Tất cả chi nhánh trong phạm vi', value: null as string | null }] : []),
  ...auth.branches.map((b) => ({ label: `${b.code} · ${b.name}`, value: b.id as string | null })),
])
const maxDay = computed(() => Math.max(1, ...(report.value?.byDay.map((d) => d.paidRevenue) ?? [1])))
const maxBranch = computed(() => Math.max(1, ...(report.value?.byBranch.map((d) => d.paidRevenue) ?? [1])))

async function load() {
  loading.value = true
  try {
    report.value = await api.get<RevenueReport>('/reports/revenue', { from: from.value, to: to.value, branchId: branchId.value })
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function exportCsv(kind: 'revenue' | 'inventory') {
  exporting.value = kind
  try {
    await api.download(`/reports/${kind}/export.csv`, { from: from.value, to: to.value, branchId: branchId.value }, `${kind}.csv`)
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không xuất được CSV', detail: errorMessage(e), life: 5000 })
  } finally {
    exporting.value = null
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Báo cáo doanh thu</h1>
        <p>Doanh thu ghi nhận theo hóa đơn đã thanh toán, theo ngày kinh doanh (Asia/Ho_Chi_Minh).</p>
      </div>
      <div class="toolbar">
        <Select v-model="branchId" :options="branchOptions" option-label="label" option-value="value" style="min-width: 220px" />
        <InputText v-model="from" type="date" aria-label="Từ ngày" />
        <InputText v-model="to" type="date" aria-label="Đến ngày" />
        <Button label="Xem" icon="pi pi-search" :loading="loading" @click="load" />
      </div>
    </div>

    <div class="row wrap" style="margin-bottom: 18px">
      <Button id="btn-export-revenue" label="Xuất CSV doanh thu" icon="pi pi-file-excel" outlined :loading="exporting === 'revenue'" @click="exportCsv('revenue')" />
      <Button id="btn-export-inventory" label="Xuất CSV biến động kho" icon="pi pi-file-excel" outlined severity="secondary" :loading="exporting === 'inventory'" @click="exportCsv('inventory')" />
      <span class="faint small">CSV UTF-8 có BOM, mở trực tiếp bằng Excel.</span>
    </div>

    <div class="grid grid-4" style="margin-bottom: 18px">
      <div class="stat"><div class="label">Doanh thu đã thu</div><div class="value">{{ money(report?.paidRevenue) }}</div><div class="hint">{{ report?.paidInvoices ?? 0 }} hóa đơn</div></div>
      <div class="stat"><div class="label">Giá trị TB</div><div class="value">{{ money(report?.averageOrderValue) }}</div><div class="hint">Thuế {{ money(report?.paidTax) }}</div></div>
      <div class="stat"><div class="label">Chưa thanh toán</div><div class="value" style="color: var(--warning)">{{ money(report?.unpaidAmount) }}</div><div class="hint">{{ report?.unpaidInvoices ?? 0 }} hóa đơn</div></div>
      <div class="stat"><div class="label">Đã hoàn tiền</div><div class="value" style="color: #ffa98a">{{ money(report?.refundedAmount) }}</div><div class="hint">{{ report?.refundedInvoices ?? 0 }} HĐ · {{ report?.voidedInvoices ?? 0 }} HĐ hủy</div></div>
    </div>

    <div class="card" style="margin-bottom: 18px">
      <div class="card-title"><h2>Theo ngày</h2></div>
      <Skeleton v-if="loading" height="200px" />
      <EmptyState v-else-if="!report?.byDay.length" icon="pi-chart-line" title="Không có doanh thu" />
      <div v-else class="bars">
        <div v-for="d in report.byDay" :key="d.date" class="bar-col" v-tooltip.top="`${d.date}: ${money(d.paidRevenue)} (${d.paidInvoices} HĐ)`">
          <div class="bar" :style="{ height: Math.max(3, (d.paidRevenue / maxDay) * 180) + 'px' }" />
          <div class="bar-label">{{ d.date.slice(8, 10) }}</div>
        </div>
      </div>
    </div>

    <div class="grid grid-2">
      <div class="card">
        <div class="card-title"><h2>Theo chi nhánh</h2></div>
        <EmptyState v-if="!report?.byBranch.length" icon="pi-building" title="Không có dữ liệu" />
        <div v-for="b in report?.byBranch ?? []" :key="b.branchId" class="hbar">
          <div class="row"><strong>{{ b.branchCode }}</strong><span class="muted small">{{ b.branchName }}</span><span class="spacer" /><MoneyText :value="b.paidRevenue" strong /></div>
          <div class="track"><div :style="{ width: (b.paidRevenue / maxBranch) * 100 + '%' }" /></div>
        </div>
      </div>
      <div class="card">
        <div class="card-title"><h2>Theo phương thức</h2></div>
        <EmptyState v-if="!report?.byMethod.length" icon="pi-wallet" title="Không có dữ liệu" />
        <DataTable v-else :value="report.byMethod" size="small">
          <Column header="Phương thức"><template #body="{ data: m }">{{ methodLabels[m.method] ?? m.method }}</template></Column>
          <Column field="count" header="Số GD" class="right" />
          <Column header="Số tiền" class="right"><template #body="{ data: m }"><MoneyText :value="m.amount" strong /></template></Column>
        </DataTable>
      </div>
    </div>
    <p v-if="report?.definition" class="faint small" style="margin-top: 16px"><i class="pi pi-info-circle" /> {{ report.definition }}</p>
  </div>
</template>

<style scoped>
.bars { display: flex; align-items: flex-end; gap: 6px; height: 210px; overflow-x: auto; }
.bar-col { flex: 1; min-width: 16px; display: flex; flex-direction: column; align-items: center; gap: 4px; }
.bar { width: 100%; max-width: 30px; border-radius: 6px 6px 2px 2px; background: var(--accent-grad); animation: grow .5s ease both; transform-origin: bottom; }
.bar-label { font-size: .68rem; color: var(--text-faint); }
@keyframes grow { from { transform: scaleY(0); } }
.hbar { margin-bottom: 14px; }
.track { height: 8px; border-radius: 8px; background: rgba(255, 255, 255, .05); margin-top: 6px; overflow: hidden; }
.track > div { height: 100%; background: var(--accent-grad); border-radius: 8px; transition: width .5s; }
</style>
