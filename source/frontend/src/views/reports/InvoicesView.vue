<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { Invoice, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { useOrderActions } from '@/composables/useOrderActions'
import { dateTime, methodLabels, money, todayLocal } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const rows = ref<Invoice[]>([])
const total = ref(0)
const page = ref(1)
const from = ref(todayLocal(-6))
const to = ref(todayLocal())
const paymentStatus = ref<string | null>(null)
const loading = ref(false)
const preview = ref<{ url: string; invoice: Invoice } | null>(null)
const { downloadPdf, printPdf } = useOrderActions(() => undefined)
const statusOptions = [
  { label: 'Đã thanh toán', value: 'Paid' },
  { label: 'Chưa thanh toán', value: 'Unpaid' },
  { label: 'Đã hoàn tiền', value: 'Refunded' },
]

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    const res = await api.get<Paged<Invoice>>('/invoices', { branchId: auth.currentBranch.id, from: from.value, to: to.value, paymentStatus: paymentStatus.value, page: page.value, pageSize: 50 })
    rows.value = res.items
    total.value = res.totalCount
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function openPreview(inv: Invoice) {
  try {
    const url = await api.blobUrl(`/invoices/${inv.id}/pdf`, { size: 'A5' })
    if (preview.value) URL.revokeObjectURL(preview.value.url)
    preview.value = { url, invoice: inv }
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không mở được PDF', detail: errorMessage(e), life: 5000 })
  }
}

function closePreview() {
  if (preview.value) URL.revokeObjectURL(preview.value.url)
  preview.value = null
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Hóa đơn</h1>
        <p>Hóa đơn nội bộ (PDF K80/A5) — không phải hóa đơn điện tử theo quy định thuế.</p>
      </div>
      <div class="toolbar">
        <InputText v-model="from" type="date" aria-label="Từ ngày" />
        <InputText v-model="to" type="date" aria-label="Đến ngày" />
        <Select v-model="paymentStatus" :options="statusOptions" option-label="label" option-value="value" placeholder="Mọi trạng thái" show-clear />
        <Button icon="pi pi-search" @click="(page = 1), load()" />
      </div>
    </div>
    <div class="card flush">
      <DataTable :value="rows" :loading="loading" lazy paginator :rows="50" :total-records="total" @page="(e) => ((page = e.page + 1), load())">
        <template #empty><EmptyState icon="pi-file-pdf" title="Không có hóa đơn" /></template>
        <Column header="Số HĐ"><template #body="{ data: i }"><span class="mono">{{ i.invoiceNo }}</span></template></Column>
        <Column header="Đơn"><template #body="{ data: i }">#{{ i.orderNumber }} <span class="faint small">{{ i.businessDate }}</span></template></Column>
        <Column header="Thanh toán">
          <template #body="{ data: i }"><StatusPill :status="i.paymentStatus" /> <span class="small muted">{{ methodLabels[i.paymentMethod ?? ''] ?? '' }}</span></template>
        </Column>
        <Column header="Tổng" class="right"><template #body="{ data: i }"><MoneyText :value="i.totalAmount" strong /></template></Column>
        <Column header="Phát hành"><template #body="{ data: i }"><span class="small">{{ dateTime(i.issuedAtUtc) }}</span></template></Column>
        <Column header="" style="width: 170px">
          <template #body="{ data: i }">
            <Button icon="pi pi-eye" text rounded v-tooltip.top="'Xem A5'" @click="openPreview(i)" />
            <Button icon="pi pi-print" text rounded v-tooltip.top="'In K80'" @click="printPdf(i.id, 'K80')" />
            <Button icon="pi pi-download" text rounded v-tooltip.top="'Tải A5'" @click="downloadPdf(i.id, 'A5')" />
          </template>
        </Column>
      </DataTable>
    </div>

    <Dialog :visible="!!preview" modal :header="preview ? `Hóa đơn ${preview.invoice.invoiceNo} · ${money(preview.invoice.totalAmount)}` : ''" :style="{ width: '760px' }" @update:visible="(v) => !v && closePreview()">
      <iframe v-if="preview" :src="preview.url" title="Xem trước hóa đơn" class="pdf" />
    </Dialog>
  </div>
</template>

<style scoped>
.pdf { width: 100%; height: 72vh; border: 0; border-radius: 10px; background: #fff; }
</style>
