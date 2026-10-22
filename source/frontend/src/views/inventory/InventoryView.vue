<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { Ingredient, InventoryDocument, Paged, Stock } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime, qty } from '@/utils/format'

type DocKind = 'receipts' | 'issues' | 'adjustments' | 'stocktakes'

const auth = useAuthStore()
const toast = useToast()
const stock = ref<Stock[]>([])
const docs = ref<InventoryDocument[]>([])
const ingredients = ref<Ingredient[]>([])
const loading = ref(false)
const onlyLow = ref(false)
const search = ref('')

const docDialog = ref(false)
const docKind = ref<DocKind>('receipts')
const docForm = reactive({ reason: '', supplier: '', lines: [] as { ingredientId: string; value: number | null; unitCost: number | null }[] })
const saving = ref(false)

const thresholdDialog = ref(false)
const thresholdForm = reactive({ ingredientId: '', minThreshold: 0 })

const kindMeta: Record<DocKind, { title: string; icon: string; valueLabel: string; admin: boolean }> = {
  receipts: { title: 'Nhập kho', icon: 'pi-download', valueLabel: 'Số lượng nhập', admin: false },
  issues: { title: 'Xuất kho (hủy/hao hụt)', icon: 'pi-upload', valueLabel: 'Số lượng xuất', admin: false },
  adjustments: { title: 'Điều chỉnh', icon: 'pi-sliders-v', valueLabel: 'Chênh lệch (+/−)', admin: true },
  stocktakes: { title: 'Kiểm kê', icon: 'pi-list-check', valueLabel: 'Số đếm thực tế', admin: true },
}

const filtered = computed(() =>
  stock.value.filter((s) => (!onlyLow.value || s.isLow) && (!search.value || (s.name + s.code).toLowerCase().includes(search.value.toLowerCase()))),
)
const lowCount = computed(() => stock.value.filter((s) => s.isLow).length)
const unitOf = (id: string) => ingredients.value.find((i) => i.id === id)?.unit ?? stock.value.find((s) => s.ingredientId === id)?.unit ?? ''
const typeLabels: Record<string, string> = { Receipt: 'Nhập', Issue: 'Xuất', Adjustment: 'Điều chỉnh', Stocktake: 'Kiểm kê' }

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  const id = auth.currentBranch.id
  try {
    const [s, d, i] = await Promise.all([
      api.get<Stock[]>(`/branches/${id}/inventory`),
      api.get<Paged<InventoryDocument>>(`/branches/${id}/inventory/documents`, { pageSize: 20 }),
      api.get<Ingredient[]>('/ingredients'),
    ])
    stock.value = s
    docs.value = d.items
    ingredients.value = i.filter((x) => x.isActive)
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function openDoc(kind: DocKind) {
  docKind.value = kind
  Object.assign(docForm, { reason: '', supplier: '', lines: [{ ingredientId: '', value: null, unitCost: null }] })
  docDialog.value = true
}

async function saveDoc() {
  if (!auth.currentBranch) return
  const lines = docForm.lines.filter((l) => l.ingredientId && l.value !== null)
  if (!lines.length) return
  const k = docKind.value
  const body =
    k === 'receipts' || k === 'issues'
      ? { reason: docForm.reason, supplier: docForm.supplier || null, lines: lines.map((l) => ({ ingredientId: l.ingredientId, quantity: l.value, unitCost: l.unitCost })) }
      : k === 'adjustments'
        ? { reason: docForm.reason, lines: lines.map((l) => ({ ingredientId: l.ingredientId, quantityChange: l.value })) }
        : { reason: docForm.reason, lines: lines.map((l) => ({ ingredientId: l.ingredientId, countedQuantity: l.value })) }
  saving.value = true
  try {
    await api.post(`/branches/${auth.currentBranch.id}/inventory/${k}`, body)
    toast.add({ severity: 'success', summary: `${kindMeta[k].title}: đã ghi sổ`, life: 2500 })
    docDialog.value = false
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không ghi được chứng từ', detail: errorMessage(e), life: 6000 })
  } finally {
    saving.value = false
  }
}

function openThreshold(s?: Stock) {
  Object.assign(thresholdForm, s ? { ingredientId: s.ingredientId, minThreshold: s.minThreshold } : { ingredientId: '', minThreshold: 0 })
  thresholdDialog.value = true
}

async function saveThreshold() {
  if (!auth.currentBranch) return
  try {
    await api.post(`/branches/${auth.currentBranch.id}/ingredients`, thresholdForm)
    thresholdDialog.value = false
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Tồn kho</h1>
        <p>{{ auth.currentBranch?.name }} · mọi biến động đều ghi vào sổ kho bất biến.</p>
      </div>
      <div class="toolbar">
        <Button label="Nhập kho" icon="pi pi-download" @click="openDoc('receipts')" />
        <Button label="Xuất kho" icon="pi pi-upload" outlined @click="openDoc('issues')" />
        <template v-if="auth.isAdmin">
          <Button label="Điều chỉnh" icon="pi pi-sliders-v" outlined severity="secondary" @click="openDoc('adjustments')" />
          <Button label="Kiểm kê" icon="pi pi-list-check" outlined severity="secondary" @click="openDoc('stocktakes')" />
        </template>
      </div>
    </div>

    <div class="grid grid-3" style="margin-bottom: 18px">
      <div class="stat"><div class="label">Nguyên liệu theo dõi</div><div class="value">{{ stock.length }}</div></div>
      <div class="stat"><div class="label">Dưới ngưỡng</div><div class="value" :style="{ color: lowCount ? 'var(--danger)' : 'var(--success)' }">{{ lowCount }}</div></div>
      <div class="stat"><div class="label">Chứng từ gần đây</div><div class="value">{{ docs.length }}</div><div class="hint"><RouterLink to="/inventory/ledger">Mở sổ kho →</RouterLink></div></div>
    </div>

    <div class="card flush" style="margin-bottom: 18px">
      <div class="row wrap" style="padding: 14px 16px">
        <h2>Tồn hiện tại</h2>
        <div class="spacer" />
        <div class="row"><ToggleSwitch v-model="onlyLow" input-id="only-low" /><label for="only-low" class="small">Chỉ dưới ngưỡng</label></div>
        <InputText v-model="search" placeholder="Tìm nguyên liệu" />
        <Button v-if="auth.isAdmin" label="Theo dõi / ngưỡng" icon="pi pi-cog" text @click="openThreshold()" />
      </div>
      <DataTable :value="filtered" :loading="loading" paginator :rows="25" size="small" :row-class="(s: Stock) => (s.isLow ? 'low-row' : '')">
        <template #empty><EmptyState icon="pi-box" title="Chưa có nguyên liệu nào được theo dõi" /></template>
        <Column field="code" header="Mã" sortable><template #body="{ data: s }"><span class="mono">{{ s.code }}</span></template></Column>
        <Column field="name" header="Nguyên liệu" sortable />
        <Column field="quantityOnHand" header="Tồn" sortable class="right">
          <template #body="{ data: s }"><strong :style="{ color: s.isLow ? 'var(--danger)' : undefined }">{{ qty(s.quantityOnHand) }}</strong> <span class="faint">{{ s.unit }}</span></template>
        </Column>
        <Column header="Ngưỡng" class="right"><template #body="{ data: s }">{{ qty(s.minThreshold) }} {{ s.unit }}</template></Column>
        <Column header="Mức">
          <template #body="{ data: s }">
            <div class="meter"><div :style="{ width: Math.min(100, s.minThreshold ? (s.quantityOnHand / (s.minThreshold * 3)) * 100 : 100) + '%' }" :class="{ low: s.isLow }" /></div>
          </template>
        </Column>
        <Column header="Cập nhật"><template #body="{ data: s }"><span class="small faint">{{ dateTime(s.updatedAtUtc) }}</span></template></Column>
        <Column v-if="auth.isAdmin" header=""><template #body="{ data: s }"><Button icon="pi pi-cog" text rounded v-tooltip.top="'Ngưỡng cảnh báo'" @click="openThreshold(s)" /></template></Column>
      </DataTable>
    </div>

    <div class="card flush">
      <div style="padding: 14px 16px"><h2>Chứng từ gần đây</h2></div>
      <DataTable :value="docs" size="small" data-key="id">
        <template #empty><EmptyState icon="pi-file" title="Chưa có chứng từ" /></template>
        <Column header="Số"><template #body="{ data: d }"><span class="mono">{{ d.documentNo }}</span></template></Column>
        <Column header="Loại"><template #body="{ data: d }"><span class="pill Confirmed">{{ typeLabels[d.type] ?? d.type }}</span></template></Column>
        <Column field="reason" header="Lý do" />
        <Column header="NCC"><template #body="{ data: d }">{{ d.supplier ?? '—' }}</template></Column>
        <Column header="Dòng" class="right"><template #body="{ data: d }">{{ d.lines.length }}</template></Column>
        <Column header="Thời gian"><template #body="{ data: d }"><span class="small">{{ dateTime(d.createdAtUtc) }}</span></template></Column>
      </DataTable>
    </div>

    <Dialog v-model:visible="docDialog" modal :header="kindMeta[docKind].title" :style="{ width: '720px' }">
      <div class="stack">
        <div class="form-grid">
          <div class="field" :class="{ full: docKind !== 'receipts' }">
            <label for="doc-reason">Lý do</label>
            <InputText id="doc-reason" v-model="docForm.reason" :placeholder="docKind === 'issues' ? 'Hư hỏng, hết hạn…' : ''" />
          </div>
          <div v-if="docKind === 'receipts'" class="field"><label for="doc-supplier">Nhà cung cấp</label><InputText id="doc-supplier" v-model="docForm.supplier" /></div>
        </div>
        <div v-for="(l, idx) in docForm.lines" :key="idx" class="row">
          <Select v-model="l.ingredientId" :options="ingredients" option-label="name" option-value="id" filter placeholder="Nguyên liệu" style="flex: 1" />
          <InputNumber v-model="l.value" :placeholder="kindMeta[docKind].valueLabel" :max-fraction-digits="3" :min="docKind === 'adjustments' ? undefined : 0" :suffix="' ' + unitOf(l.ingredientId)" style="width: 180px" />
          <InputNumber v-if="docKind === 'receipts'" v-model="l.unitCost" placeholder="Đơn giá" :min="0" locale="vi-VN" style="width: 140px" />
          <Button icon="pi pi-trash" text rounded severity="danger" :disabled="docForm.lines.length === 1" @click="docForm.lines.splice(idx, 1)" />
        </div>
        <div><Button label="Thêm dòng" icon="pi pi-plus" text size="small" @click="docForm.lines.push({ ingredientId: '', value: null, unitCost: null })" /></div>
        <Message v-if="docKind === 'stocktakes'" severity="info" :closable="false">Hệ thống ghi chênh lệch giữa số đếm và tồn sổ sách tại thời điểm lưu.</Message>
        <Message v-if="docKind === 'issues' || docKind === 'adjustments'" severity="warn" :closable="false">Không cho phép xuất/điều chỉnh làm tồn kho âm.</Message>
      </div>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="docDialog = false" />
        <Button id="btn-save-doc" label="Ghi sổ" icon="pi pi-check" :loading="saving" :disabled="!docForm.reason.trim()" @click="saveDoc" />
      </template>
    </Dialog>

    <Dialog v-model:visible="thresholdDialog" modal header="Theo dõi nguyên liệu & ngưỡng cảnh báo" :style="{ width: '440px' }">
      <div class="stack">
        <div class="field">
          <label for="th-ing">Nguyên liệu</label>
          <Select input-id="th-ing" v-model="thresholdForm.ingredientId" :options="ingredients" option-label="name" option-value="id" filter />
        </div>
        <div class="field">
          <label for="th-min">Ngưỡng tối thiểu ({{ unitOf(thresholdForm.ingredientId) }})</label>
          <InputNumber input-id="th-min" v-model="thresholdForm.minThreshold" :min="0" :max-fraction-digits="3" />
        </div>
      </div>
      <template #footer><Button label="Lưu" icon="pi pi-check" :disabled="!thresholdForm.ingredientId" @click="saveThreshold" /></template>
    </Dialog>
  </div>
</template>

<style scoped>
.meter { width: 110px; height: 6px; border-radius: 6px; background: rgba(255, 255, 255, .06); overflow: hidden; }
.meter > div { height: 100%; background: linear-gradient(90deg, #4fd1a1, #7cc4ff); border-radius: 6px; transition: width .4s; }
.meter > div.low { background: linear-gradient(90deg, #ff6b6b, #ffcc66); }
:deep(.low-row) { background: rgba(255, 107, 107, .05) !important; }
</style>
