<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import QRCode from 'qrcode'
import { api, ApiError, errorMessage } from '@/api/http'
import type { Branch, BranchTable } from '@/api/types'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const toast = useToast()
const branches = ref<Branch[]>([])
const loading = ref(true)
const dialog = ref(false)
const saving = ref(false)
const editing = ref<Branch | null>(null)
const fieldErrors = ref<Record<string, string>>({})
const form = reactive({
  code: '', name: '', address: '', phone: '', timeZoneId: 'Asia/Ho_Chi_Minh', isActive: true, publicOrderingEnabled: true,
  invoiceHeader: '', invoiceFooter: '', taxRatePercent: 0, pricesIncludeTax: true, roundingUnit: 0, allowNegativeStock: false,
})

const tableDialog = ref(false)
const tableBranch = ref<Branch | null>(null)
const newTableName = ref('')
const qrDialog = ref(false)
const qr = reactive({ url: '', image: '', title: '' })

async function load() {
  loading.value = true
  try {
    branches.value = await api.get<Branch[]>('/branches')
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editing.value = null
  Object.assign(form, {
    code: '', name: '', address: '', phone: '', timeZoneId: 'Asia/Ho_Chi_Minh', isActive: true, publicOrderingEnabled: true,
    invoiceHeader: '', invoiceFooter: '', taxRatePercent: 0, pricesIncludeTax: true, roundingUnit: 0, allowNegativeStock: false,
  })
  fieldErrors.value = {}
  dialog.value = true
}

function openEdit(b: Branch) {
  editing.value = b
  Object.assign(form, { ...b, phone: b.phone ?? '', invoiceHeader: b.invoiceHeader ?? '', invoiceFooter: b.invoiceFooter ?? '' })
  fieldErrors.value = {}
  dialog.value = true
}

async function save() {
  saving.value = true
  fieldErrors.value = {}
  try {
    if (editing.value) {
      const { code: _code, ...rest } = form
      await api.put(`/branches/${editing.value.id}`, { ...rest, rowVersion: editing.value.rowVersion })
    } else {
      await api.post('/branches', form)
    }
    toast.add({ severity: 'success', summary: 'Đã lưu chi nhánh', life: 2500 })
    dialog.value = false
    await load()
    if (!editing.value) await auth.loadMe()
  } catch (e) {
    if (e instanceof ApiError) fieldErrors.value = e.fieldErrors
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  } finally {
    saving.value = false
  }
}

function openTables(b: Branch) {
  tableBranch.value = b
  newTableName.value = ''
  tableDialog.value = true
}

async function addTable() {
  if (!tableBranch.value || !newTableName.value.trim()) return
  try {
    const t = await api.post<BranchTable>(`/branches/${tableBranch.value.id}/tables`, { name: newTableName.value.trim() })
    tableBranch.value.tables.push(t)
    newTableName.value = ''
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

async function toggleTable(t: BranchTable) {
  if (!tableBranch.value) return
  try {
    const updated = await api.put<BranchTable>(`/branches/${tableBranch.value.id}/tables/${t.id}`, { name: t.name, isActive: !t.isActive })
    Object.assign(t, updated)
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

function orderUrl(b: Branch, t?: BranchTable) {
  return `${window.location.origin}/order/${b.code}${t ? `?table=${encodeURIComponent(t.publicCode)}` : ''}`
}

async function showQr(b: Branch, t?: BranchTable) {
  qr.url = orderUrl(b, t)
  qr.title = t ? `${b.name} · ${t.name}` : `${b.name} · Mang đi`
  qr.image = await QRCode.toDataURL(qr.url, { width: 320, margin: 2, color: { dark: '#1b120d', light: '#ffffff' } })
  qrDialog.value = true
}

async function copy(text: string) {
  await navigator.clipboard.writeText(text)
  toast.add({ severity: 'info', summary: 'Đã sao chép liên kết', life: 1500 })
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Chi nhánh</h1>
        <p>Cấu hình chi nhánh, bàn và mã QR gọi món.</p>
      </div>
      <Button v-if="auth.isSuperAdmin" id="btn-new-branch" label="Thêm chi nhánh" icon="pi pi-plus" @click="openCreate" />
    </div>

    <div v-if="loading" class="grid grid-2"><Skeleton v-for="i in 2" :key="i" height="220px" /></div>
    <EmptyState v-else-if="!branches.length" icon="pi-building" title="Chưa có chi nhánh" />
    <div v-else class="grid grid-2">
      <div v-for="b in branches" :key="b.id" class="card branch-card">
        <div class="card-title">
          <div>
            <div class="row"><h2>{{ b.name }}</h2><span class="pill" :class="b.isActive ? 'Ready' : 'Cancelled'">{{ b.isActive ? 'Hoạt động' : 'Tạm dừng' }}</span></div>
            <div class="faint small mono">{{ b.code }} · {{ b.timeZoneId }}</div>
          </div>
          <Button icon="pi pi-pencil" text rounded aria-label="Sửa" @click="openEdit(b)" />
        </div>
        <div class="stack small">
          <div><i class="pi pi-map-marker faint" /> {{ b.address }}</div>
          <div v-if="b.phone"><i class="pi pi-phone faint" /> {{ b.phone }}</div>
          <div class="row wrap">
            <span class="pill" :class="b.publicOrderingEnabled ? 'Confirmed' : 'Unpaid'">QR order {{ b.publicOrderingEnabled ? 'bật' : 'tắt' }}</span>
            <span class="pill Unpaid">Thuế {{ b.taxRatePercent }}% {{ b.pricesIncludeTax ? '(đã gồm)' : '(cộng thêm)' }}</span>
            <span class="pill Unpaid">Làm tròn {{ b.roundingUnit || 'không' }}</span>
          </div>
        </div>
        <Divider />
        <div class="row">
          <span class="muted small">{{ b.tables.filter((t) => t.isActive).length }} bàn đang dùng</span>
          <div class="spacer" />
          <Button label="Mã QR mang đi" icon="pi pi-qrcode" size="small" text @click="showQr(b)" />
          <Button label="Quản lý bàn" icon="pi pi-table" size="small" outlined @click="openTables(b)" />
          <a :href="`/queue/${b.code}`" target="_blank" rel="noopener"><Button icon="pi pi-desktop" size="small" text v-tooltip.top="'Màn hình gọi số'" /></a>
        </div>
      </div>
    </div>

    <Dialog v-model:visible="dialog" modal :header="editing ? 'Sửa chi nhánh' : 'Thêm chi nhánh'" :style="{ width: '640px' }">
      <form class="form-grid" @submit.prevent="save">
        <div class="field">
          <label for="br-code">Mã chi nhánh</label>
          <InputText id="br-code" v-model="form.code" :disabled="!!editing" :invalid="!!fieldErrors.code" />
          <small class="error">{{ fieldErrors.code }}</small>
        </div>
        <div class="field">
          <label for="br-name">Tên</label>
          <InputText id="br-name" v-model="form.name" :invalid="!!fieldErrors.name" />
          <small class="error">{{ fieldErrors.name }}</small>
        </div>
        <div class="field full">
          <label for="br-address">Địa chỉ</label>
          <InputText id="br-address" v-model="form.address" :invalid="!!fieldErrors.address" />
          <small class="error">{{ fieldErrors.address }}</small>
        </div>
        <div class="field"><label for="br-phone">Điện thoại</label><InputText id="br-phone" v-model="form.phone" /></div>
        <div class="field"><label for="br-tz">Múi giờ</label><InputText id="br-tz" v-model="form.timeZoneId" /></div>
        <div class="field">
          <label for="br-tax">Thuế suất (%)</label>
          <InputNumber input-id="br-tax" v-model="form.taxRatePercent" :min="0" :max="100" :max-fraction-digits="2" />
        </div>
        <div class="field">
          <label for="br-round">Làm tròn (VND, 0 = không)</label>
          <InputNumber input-id="br-round" v-model="form.roundingUnit" :min="0" :step="100" />
        </div>
        <div class="field full"><label for="br-header">Tiêu đề hóa đơn</label><Textarea id="br-header" v-model="form.invoiceHeader" rows="2" auto-resize /></div>
        <div class="field full"><label for="br-footer">Chân hóa đơn</label><Textarea id="br-footer" v-model="form.invoiceFooter" rows="2" auto-resize /></div>
        <div class="row"><ToggleSwitch v-model="form.pricesIncludeTax" input-id="br-incl" /><label for="br-incl">Giá đã gồm thuế</label></div>
        <template v-if="editing">
          <div class="row"><ToggleSwitch v-model="form.publicOrderingEnabled" input-id="br-qr" /><label for="br-qr">Nhận đơn QR</label></div>
          <div class="row"><ToggleSwitch v-model="form.isActive" input-id="br-active" /><label for="br-active">Đang hoạt động</label></div>
          <div class="row"><ToggleSwitch v-model="form.allowNegativeStock" input-id="br-neg" disabled /><label for="br-neg" class="muted">Cho phép âm kho (không hỗ trợ ở MVP)</label></div>
        </template>
      </form>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="dialog = false" />
        <Button id="btn-save-branch" label="Lưu" icon="pi pi-check" :loading="saving" @click="save" />
      </template>
    </Dialog>

    <Dialog v-model:visible="tableDialog" modal :header="`Bàn · ${tableBranch?.name ?? ''}`" :style="{ width: '640px' }">
      <form class="row" style="margin-bottom: 14px" @submit.prevent="addTable">
        <InputText v-model="newTableName" placeholder="Tên bàn mới, ví dụ: Bàn 06" class="w-full" />
        <Button type="submit" label="Thêm" icon="pi pi-plus" :disabled="!newTableName.trim()" />
      </form>
      <DataTable :value="tableBranch?.tables ?? []" size="small">
        <Column field="name" header="Bàn" />
        <Column header="Mã công khai"><template #body="{ data: t }"><span class="mono small">{{ t.publicCode }}</span></template></Column>
        <Column header="Trạng thái">
          <template #body="{ data: t }"><ToggleSwitch :model-value="t.isActive" @update:model-value="toggleTable(t)" /></template>
        </Column>
        <Column header="">
          <template #body="{ data: t }">
            <Button icon="pi pi-qrcode" text rounded :disabled="!t.isActive" v-tooltip.top="'Mã QR'" @click="tableBranch && showQr(tableBranch, t)" />
          </template>
        </Column>
      </DataTable>
    </Dialog>

    <Dialog v-model:visible="qrDialog" modal :header="qr.title" :style="{ width: '400px' }">
      <div class="qr-box">
        <img :src="qr.image" alt="Mã QR gọi món" width="280" height="280" />
        <div class="mono small muted qr-url">{{ qr.url }}</div>
        <div class="row">
          <Button label="Sao chép" icon="pi pi-copy" text @click="copy(qr.url)" />
          <a :href="qr.image" :download="`qr-${qr.title}.png`"><Button label="Tải PNG" icon="pi pi-download" outlined /></a>
        </div>
      </div>
    </Dialog>
  </div>
</template>

<style scoped>
.branch-card { transition: transform .2s, box-shadow .2s; }
.branch-card:hover { transform: translateY(-2px); box-shadow: var(--shadow-glow); }
.qr-box { display: flex; flex-direction: column; align-items: center; gap: 12px; }
.qr-box img { border-radius: 14px; background: #fff; padding: 8px; }
.qr-url { word-break: break-all; text-align: center; }
</style>
