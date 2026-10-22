<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, errorMessage } from '@/api/http'
import type { Dashboard } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { money, qty, todayLocal } from '@/utils/format'

const auth = useAuthStore()
const data = ref<Dashboard | null>(null)
const loading = ref(true)
const error = ref('')
const range = ref<'today' | '7d' | '30d'>('today')
const scope = ref<'branch' | 'all'>('branch')
const rangeOptions = [
  { label: 'Hôm nay', value: 'today' },
  { label: '7 ngày', value: '7d' },
  { label: '30 ngày', value: '30d' },
]
const scopeOptions = [
  { label: 'Chi nhánh hiện tại', value: 'branch' },
  { label: 'Tất cả chi nhánh', value: 'all' },
]

async function load() {
  loading.value = true
  error.value = ''
  const days = range.value === 'today' ? 0 : range.value === '7d' ? 6 : 29
  try {
    data.value = await api.get<Dashboard>('/reports/dashboard', {
      from: todayLocal(-days),
      to: todayLocal(),
      branchId: scope.value === 'branch' || !auth.isAdmin ? auth.currentBranch?.id : undefined,
    })
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}

const maxDay = computed(() => Math.max(1, ...(data.value?.revenueByDay.map((d) => d.paidRevenue) ?? [1])))
const activeCount = computed(() =>
  (data.value?.ordersByStatus ?? []).filter((s) => ['Submitted', 'PendingConfirmation', 'Confirmed', 'Preparing', 'Ready'].includes(s.status)).reduce((a, s) => a + s.count, 0),
)

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Xin chào, <span class="gradient-text">{{ auth.me?.displayName }}</span></h1>
        <p>Tổng quan hoạt động {{ scope === 'all' && auth.isAdmin ? 'toàn chuỗi' : auth.currentBranch?.name }}.</p>
      </div>
      <div class="toolbar">
        <SelectButton v-if="auth.isAdmin && auth.branches.length > 1" v-model="scope" :options="scopeOptions" option-label="label" option-value="value" :allow-empty="false" @change="load" />
        <SelectButton v-model="range" :options="rangeOptions" option-label="label" option-value="value" :allow-empty="false" @change="load" />
        <Button icon="pi pi-refresh" text rounded aria-label="Tải lại" @click="load" />
      </div>
    </div>

    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

    <div class="grid grid-4" style="margin-bottom: 18px">
      <div class="stat">
        <div class="label">Doanh thu đã thu</div>
        <div class="value"><Skeleton v-if="loading" height="2rem" /><template v-else>{{ money(data?.paidRevenue) }}</template></div>
        <div class="hint">{{ data?.paidInvoices ?? 0 }} hóa đơn đã thanh toán</div>
      </div>
      <div class="stat">
        <div class="label">Giá trị TB / đơn</div>
        <div class="value"><Skeleton v-if="loading" height="2rem" /><template v-else>{{ money(data?.averageOrderValue) }}</template></div>
        <div class="hint">Trên hóa đơn đã thanh toán</div>
      </div>
      <div class="stat">
        <div class="label">Tổng đơn</div>
        <div class="value"><Skeleton v-if="loading" height="2rem" /><template v-else>{{ data?.totalOrders ?? 0 }}</template></div>
        <div class="hint">{{ activeCount }} đơn đang xử lý</div>
      </div>
      <div class="stat">
        <div class="label">Giờ công</div>
        <div class="value"><Skeleton v-if="loading" height="2rem" /><template v-else>{{ data?.workedHours != null ? qty(data.workedHours) + 'h' : '—' }}</template></div>
        <div class="hint">{{ data?.shiftCount ?? 0 }} lượt chấm công</div>
      </div>
    </div>

    <div class="grid" style="grid-template-columns: 2fr 1fr; margin-bottom: 18px">
      <div class="card">
        <div class="card-title"><h2>Doanh thu theo ngày</h2><span class="faint small">VND · đã thanh toán</span></div>
        <div v-if="loading"><Skeleton height="180px" /></div>
        <EmptyState v-else-if="!data?.revenueByDay.length" icon="pi-chart-bar" title="Chưa có doanh thu trong khoảng này" />
        <div v-else class="bars">
          <div v-for="d in data.revenueByDay" :key="d.date" class="bar-col" v-tooltip.top="`${d.date}: ${money(d.paidRevenue)} · ${d.paidInvoices} HĐ`">
            <div class="bar" :style="{ height: Math.max(4, (d.paidRevenue / maxDay) * 170) + 'px' }" />
            <div class="bar-label">{{ d.date.slice(8, 10) }}/{{ d.date.slice(5, 7) }}</div>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="card-title"><h2>Trạng thái đơn</h2></div>
        <div v-if="loading"><Skeleton height="180px" /></div>
        <EmptyState v-else-if="!data?.ordersByStatus.length" icon="pi-receipt" title="Chưa có đơn" />
        <div v-else class="stack">
          <div v-for="s in data.ordersByStatus" :key="s.status" class="row">
            <StatusPill :status="s.status" />
            <div class="spacer" />
            <strong>{{ s.count }}</strong>
          </div>
        </div>
      </div>
    </div>

    <div class="grid grid-2">
      <div class="card">
        <div class="card-title"><h2>Món bán chạy</h2></div>
        <EmptyState v-if="!loading && !data?.topItems.length" icon="pi-star" title="Chưa có dữ liệu bán hàng" />
        <DataTable v-else :value="data?.topItems ?? []" :loading="loading" size="small">
          <Column field="itemName" header="Món" />
          <Column header="SL" class="right"><template #body="{ data: r }">{{ r.quantity }}</template></Column>
          <Column header="Doanh thu" class="right"><template #body="{ data: r }"><MoneyText :value="r.revenue" /></template></Column>
        </DataTable>
      </div>
      <div class="card">
        <div class="card-title">
          <h2>Cảnh báo tồn kho thấp</h2>
          <RouterLink to="/inventory" class="small">Xem kho →</RouterLink>
        </div>
        <EmptyState v-if="!loading && !data?.lowStock.length" icon="pi-check-circle" title="Không có nguyên liệu dưới ngưỡng" />
        <DataTable v-else :value="data?.lowStock ?? []" :loading="loading" size="small">
          <Column field="branchCode" header="CN" />
          <Column field="ingredientName" header="Nguyên liệu" />
          <Column header="Tồn" class="right">
            <template #body="{ data: r }"><span style="color: var(--danger); font-weight: 700">{{ qty(r.quantityOnHand) }}</span> {{ r.unit }}</template>
          </Column>
          <Column header="Ngưỡng" class="right"><template #body="{ data: r }">{{ qty(r.minThreshold) }} {{ r.unit }}</template></Column>
        </DataTable>
      </div>
    </div>

    <p v-if="data?.definitions" class="faint small" style="margin-top: 18px">
      <i class="pi pi-info-circle" /> {{ Object.values(data.definitions).join(' · ') }}
    </p>
  </div>
</template>

<style scoped>
.bars { display: flex; align-items: flex-end; gap: 8px; height: 200px; padding-top: 10px; overflow-x: auto; }
.bar-col { flex: 1; min-width: 22px; display: flex; flex-direction: column; align-items: center; gap: 6px; }
.bar { width: 100%; max-width: 36px; border-radius: 8px 8px 3px 3px; background: var(--accent-grad); box-shadow: 0 6px 20px -8px rgba(255, 122, 69, .6); transition: transform .2s; transform-origin: bottom; animation: grow .5s ease both; }
.bar-col:hover .bar { transform: scaleY(1.04); filter: brightness(1.1); }
.bar-label { font-size: .7rem; color: var(--text-faint); }
@keyframes grow { from { transform: scaleY(0); } to { transform: scaleY(1); } }
@media (max-width: 1100px) { .grid[style*='2fr'] { grid-template-columns: 1fr !important; } }
</style>
