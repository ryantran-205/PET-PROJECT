<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { OrderSummary, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { useOrdersHub } from '@/realtime/useOrdersHub'
import { chime } from '@/composables/useOrderActions'
import OrderDetailDrawer from '@/components/OrderDetailDrawer.vue'
import { elapsed, statusLabels, timeOnly, todayLocal } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const rows = ref<OrderSummary[]>([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)
const mode = ref<'active' | 'all'>('active')
const status = ref<string | null>(null)
const date = ref(todayLocal())
const orderNumber = ref<number | null>(null)
const selected = ref<string | null>(null)
const highlight = ref<Set<string>>(new Set())
const now = ref(Date.now())
const ticker = setInterval(() => (now.value = Date.now()), 30000)
onBeforeUnmount(() => clearInterval(ticker))

const statusOptions = [
  'Submitted', 'PendingConfirmation', 'Confirmed', 'Preparing', 'Ready', 'Completed', 'Cancelled', 'Rejected',
].map((s) => ({ label: statusLabels[s], value: s }))
const modeOptions = [
  { label: 'Đang xử lý', value: 'active' },
  { label: 'Tất cả', value: 'all' },
]

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    const res = await api.get<Paged<OrderSummary>>(`/branches/${auth.currentBranch.id}/orders`, {
      activeOnly: mode.value === 'active',
      status: mode.value === 'all' ? status.value : undefined,
      from: mode.value === 'all' ? date.value : undefined,
      to: mode.value === 'all' ? date.value : undefined,
      orderNumber: orderNumber.value ?? undefined,
      page: page.value,
      pageSize: 50,
    })
    rows.value = res.items
    total.value = res.totalCount
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

const counts = computed(() => {
  const c: Record<string, number> = {}
  rows.value.forEach((r) => (c[r.status] = (c[r.status] ?? 0) + 1))
  return c
})

const drawer = ref<InstanceType<typeof OrderDetailDrawer> | null>(null)
let reloadTimer: ReturnType<typeof setTimeout> | undefined
function scheduleReload() {
  clearTimeout(reloadTimer)
  reloadTimer = setTimeout(load, 250)
}

const hub = useOrdersHub({
  onJoin: async (conn) => {
    if (auth.currentBranch) await conn.invoke('JoinBranchStaff', auth.currentBranch.id)
  },
  onResync: load,
  handlers: {
    orderCreated: (e: { orderId: string; orderNumber: number }) => {
      chime()
      toast.add({ severity: 'info', summary: `Đơn mới #${e.orderNumber}`, detail: 'Khách vừa gửi đơn qua QR.', life: 5000 })
      highlight.value.add(e.orderId)
      setTimeout(() => highlight.value.delete(e.orderId), 6000)
      scheduleReload()
    },
    orderStatusChanged: (e: { orderId: string }) => {
      scheduleReload()
      if (selected.value === e.orderId) drawer.value?.reload()
    },
    paymentChanged: (e: { orderId: string }) => {
      scheduleReload()
      if (selected.value === e.orderId) drawer.value?.reload()
    },
  },
})

onMounted(() => {
  load()
  hub.start()
})
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Đơn hàng</h1>
        <p class="row">
          <span class="live" :class="hub.state.value" />
          {{ hub.state.value === 'connected' ? 'Cập nhật thời gian thực' : hub.state.value === 'reconnecting' ? 'Đang kết nối lại…' : 'Mất kết nối realtime — dữ liệu có thể chưa mới nhất' }}
        </p>
      </div>
      <div class="toolbar">
        <SelectButton v-model="mode" :options="modeOptions" option-label="label" option-value="value" :allow-empty="false" @change="(page = 1), load()" />
        <template v-if="mode === 'all'">
          <InputText v-model="date" type="date" aria-label="Ngày" @change="load" />
          <Select v-model="status" :options="statusOptions" option-label="label" option-value="value" placeholder="Trạng thái" show-clear @change="load">
            <template #option="{ option }"><StatusPill :status="option.value" /></template>
          </Select>
        </template>
        <InputNumber v-model="orderNumber" placeholder="Số đơn" :use-grouping="false" style="width: 120px" @keyup.enter="load" />
        <Button icon="pi pi-refresh" text rounded aria-label="Tải lại" @click="load" />
      </div>
    </div>

    <div v-if="mode === 'active'" class="row wrap" style="margin-bottom: 14px">
      <div v-for="s in ['Submitted', 'PendingConfirmation', 'Confirmed', 'Preparing', 'Ready']" :key="s" class="count-chip">
        <StatusPill :status="s" /> <strong>{{ counts[s] ?? 0 }}</strong>
      </div>
    </div>

    <div class="card flush">
      <DataTable
        :value="rows"
        :loading="loading"
        lazy
        paginator
        :rows="50"
        :total-records="total"
        data-key="id"
        :row-class="(r: OrderSummary) => [highlight.has(r.id) ? 'flash' : '', r.needsAttention ? 'attention' : ''].join(' ')"
        selection-mode="single"
        @row-click="(e) => (selected = e.data.id)"
        @page="(e) => ((page = e.page + 1), load())"
      >
        <template #empty><EmptyState icon="pi-receipt" title="Không có đơn" :message="mode === 'active' ? 'Đơn mới sẽ tự hiện ở đây.' : ''" /></template>
        <Column header="Số"><template #body="{ data: r }"><span class="num">#{{ r.orderNumber }}</span></template></Column>
        <Column header="Bàn"><template #body="{ data: r }">{{ r.tableName ?? 'Mang đi' }}</template></Column>
        <Column header="Món">
          <template #body="{ data: r }">
            <div class="small items-preview">{{ r.items.map((i: any) => `${i.quantity}× ${i.itemName} (${i.variantName})`).join(', ') }}</div>
          </template>
        </Column>
        <Column header="Trạng thái">
          <template #body="{ data: r }">
            <div class="row" style="gap: 6px"><StatusPill :status="r.status" /><i v-if="r.needsAttention" class="pi pi-exclamation-triangle" style="color: var(--warning)" v-tooltip.top="'Cần kiểm tra'" /></div>
          </template>
        </Column>
        <Column header="Thanh toán"><template #body="{ data: r }"><StatusPill :status="r.paymentStatus" /></template></Column>
        <Column header="Tổng" class="right"><template #body="{ data: r }"><MoneyText :value="r.totalAmount" strong /></template></Column>
        <Column header="Tạo lúc">
          <template #body="{ data: r }">
            <div class="small">{{ timeOnly(r.createdAtUtc) }}</div>
            <div v-if="mode === 'active'" class="faint small">{{ elapsed(r.createdAtUtc, now) }} trước</div>
          </template>
        </Column>
      </DataTable>
    </div>

    <OrderDetailDrawer ref="drawer" :order-id="selected" @close="selected = null" @changed="scheduleReload" />
  </div>
</template>

<style scoped>
.num { font-weight: 800; font-size: 1.05rem; }
.items-preview { max-width: 380px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: var(--text-muted); }
.count-chip { display: flex; align-items: center; gap: 8px; padding: 6px 12px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); }
.live { width: 9px; height: 9px; border-radius: 50%; background: var(--text-faint); display: inline-block; }
.live.connected { background: var(--success); box-shadow: 0 0 0 0 rgba(79, 209, 161, .6); animation: live 2s infinite; }
.live.reconnecting, .live.connecting { background: var(--warning); }
.live.disconnected { background: var(--danger); }
@keyframes live { 0% { box-shadow: 0 0 0 0 rgba(79, 209, 161, .6); } 100% { box-shadow: 0 0 0 10px rgba(79, 209, 161, 0); } }
:deep(.flash) { animation: flash 1.2s ease 3; }
:deep(.attention) { box-shadow: inset 3px 0 0 var(--warning); }
:deep(tr) { cursor: pointer; }
@keyframes flash { 0%, 100% { background: transparent; } 50% { background: rgba(255, 179, 92, .16); } }
</style>
