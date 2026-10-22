<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, errorMessage } from '@/api/http'
import type { LedgerEntry, Paged, Stock } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime, qty } from '@/utils/format'

const auth = useAuthStore()
const rows = ref<LedgerEntry[]>([])
const total = ref(0)
const page = ref(1)
const ingredientId = ref<string | null>(null)
const entryType = ref<string | null>(null)
const stock = ref<Stock[]>([])
const loading = ref(false)
const error = ref('')
const entryTypes = [
  { label: 'Nhập', value: 'Receipt' },
  { label: 'Xuất', value: 'Issue' },
  { label: 'Điều chỉnh', value: 'Adjustment' },
  { label: 'Kiểm kê', value: 'Stocktake' },
  { label: 'Trừ theo đơn', value: 'OrderConsumption' },
  { label: 'Hoàn kho (hủy đơn)', value: 'OrderReversal' },
]
const typeLabel = (t: string) => entryTypes.find((e) => e.value === t)?.label ?? t

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  error.value = ''
  try {
    const res = await api.get<Paged<LedgerEntry>>(`/branches/${auth.currentBranch.id}/inventory/ledger`, {
      ingredientId: ingredientId.value,
      entryType: entryType.value,
      page: page.value,
      pageSize: 50,
    })
    rows.value = res.items
    total.value = res.totalCount
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  if (auth.currentBranch) stock.value = await api.get<Stock[]>(`/branches/${auth.currentBranch.id}/inventory`).catch(() => [])
  load()
})
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Sổ kho</h1>
        <p>Nhật ký biến động bất biến — không sửa/xóa; hủy đơn tạo bút toán hoàn kho.</p>
      </div>
      <div class="toolbar">
        <Select v-model="ingredientId" :options="stock" option-label="name" option-value="ingredientId" placeholder="Tất cả nguyên liệu" show-clear filter @change="(page = 1), load()" />
        <Select v-model="entryType" :options="entryTypes" option-label="label" option-value="value" placeholder="Mọi loại" show-clear @change="(page = 1), load()" />
      </div>
    </div>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <div class="card flush">
      <DataTable :value="rows" :loading="loading" lazy paginator :rows="50" :total-records="total" size="small" @page="(e) => ((page = e.page + 1), load())">
        <template #empty><EmptyState icon="pi-history" title="Chưa có biến động" /></template>
        <Column header="Thời gian"><template #body="{ data: r }"><span class="small nowrap">{{ dateTime(r.createdAtUtc) }}</span></template></Column>
        <Column header="Nguyên liệu"><template #body="{ data: r }">{{ r.ingredientName }} <span class="faint mono small">{{ r.ingredientCode }}</span></template></Column>
        <Column header="Loại"><template #body="{ data: r }"><span class="pill" :class="r.quantityChange < 0 ? 'Refunded' : 'Ready'">{{ typeLabel(r.entryType) }}</span></template></Column>
        <Column header="Biến động" class="right">
          <template #body="{ data: r }">
            <strong :style="{ color: r.quantityChange < 0 ? 'var(--danger)' : 'var(--success)' }">{{ r.quantityChange > 0 ? '+' : '' }}{{ qty(r.quantityChange) }}</strong> {{ r.unit }}
          </template>
        </Column>
        <Column header="Tồn sau" class="right"><template #body="{ data: r }">{{ qty(r.balanceAfter) }} {{ r.unit }}</template></Column>
        <Column header="Nguồn"><template #body="{ data: r }"><span class="small muted">{{ r.sourceType }}</span></template></Column>
        <Column header="Lý do"><template #body="{ data: r }"><span class="small">{{ r.reason ?? '—' }}</span></template></Column>
      </DataTable>
    </div>
  </div>
</template>
