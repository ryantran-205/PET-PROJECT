<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { OrderDetail } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { actionMeta, useOrderActions } from '@/composables/useOrderActions'
import { dateTime, methodLabels, money, statusLabels } from '@/utils/format'

const props = defineProps<{ orderId: string | null }>()
const emit = defineEmits<{ close: []; changed: [] }>()

const auth = useAuthStore()
const toast = useToast()
const order = ref<OrderDetail | null>(null)
const loading = ref(false)
const visible = computed({ get: () => !!props.orderId, set: (v) => !v && emit('close') })

async function load() {
  if (!props.orderId) return
  loading.value = true
  try {
    order.value = await api.get<OrderDetail>(`/orders/${props.orderId}`)
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}
watch(() => props.orderId, (id) => (id ? load() : (order.value = null)), { immediate: true })
defineExpose({ reload: load })

const { busy, transition, ensureInvoice, downloadPdf, printPdf } = useOrderActions(() => {
  load()
  emit('changed')
})

const forwardActions = computed(() => (order.value?.allowedNextStatuses ?? []).filter((s) => !actionMeta[s]?.needsReason))
const destructiveActions = computed(() => (order.value?.allowedNextStatuses ?? []).filter((s) => actionMeta[s]?.needsReason))

const reasonDialog = reactive({ open: false, to: '', reason: '' })
function askReason(to: string) {
  Object.assign(reasonDialog, { open: true, to, reason: '' })
}
async function confirmReason() {
  if (!order.value) return
  if (await transition(order.value, reasonDialog.to, reasonDialog.reason)) reasonDialog.open = false
}

const pay = reactive({ open: false, method: 'Cash', tendered: 0 as number | null, reference: '', note: '', saving: false })
const methodOptions = [
  { label: 'Tiền mặt', value: 'Cash', icon: 'pi-money-bill' },
  { label: 'Chuyển khoản QR', value: 'BankTransferQr', icon: 'pi-qrcode' },
]
const change = computed(() => (order.value && pay.method === 'Cash' && pay.tendered ? pay.tendered - order.value.totalAmount : 0))
const quickCash = computed(() => {
  const t = order.value?.totalAmount ?? 0
  const set = new Set([t, Math.ceil(t / 50000) * 50000, Math.ceil(t / 100000) * 100000, Math.ceil(t / 200000) * 200000, 500000])
  return [...set].filter((v) => v >= t).sort((a, b) => a - b).slice(0, 4)
})
function openPay() {
  Object.assign(pay, { open: true, method: 'Cash', tendered: order.value?.totalAmount ?? 0, reference: '', note: '', saving: false })
}
async function submitPay() {
  if (!order.value) return
  pay.saving = true
  try {
    await api.post(`/orders/${order.value.id}/payments`, {
      method: pay.method,
      amount: order.value.totalAmount,
      amountTendered: pay.method === 'Cash' ? pay.tendered : null,
      reference: pay.reference || null,
      note: pay.note || null,
    })
    toast.add({ severity: 'success', summary: 'Đã ghi nhận thanh toán', detail: change.value > 0 ? `Tiền thối: ${money(change.value)}` : undefined, life: 4000 })
    pay.open = false
    await load()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không ghi nhận được', detail: errorMessage(e), life: 5000 })
  } finally {
    pay.saving = false
  }
}

const refund = reactive({ open: false, reason: '' })
async function submitRefund() {
  if (!order.value) return
  try {
    await api.post(`/orders/${order.value.id}/refund`, { reason: refund.reason })
    refund.open = false
    toast.add({ severity: 'success', summary: 'Đã ghi nhận hoàn tiền', life: 3000 })
    await load()
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không hoàn tiền được', detail: errorMessage(e), life: 5000 })
  }
}

async function invoice(size: 'K80' | 'A5', print: boolean) {
  if (!order.value) return
  let id = order.value.invoiceId
  if (!id) {
    const inv = await ensureInvoice(order.value)
    if (!inv) return
    id = inv.id
    order.value.invoiceId = id
  }
  if (print) await printPdf(id, size)
  else await downloadPdf(id, size)
}

const canPay = computed(() => order.value && order.value.paymentStatus !== 'Paid' && order.value.paymentStatus !== 'Refunded' && !['Cancelled', 'Rejected'].includes(order.value.status))
const canRefund = computed(() => auth.isAdmin && order.value?.paymentStatus === 'Paid')
</script>

<template>
  <Drawer v-model:visible="visible" position="right" class="order-drawer" :style="{ width: '560px' }">
    <template #header>
      <div v-if="order" class="row">
        <div class="num gradient-text">#{{ order.orderNumber }}</div>
        <div>
          <StatusPill :status="order.status" />
          <div class="faint small">{{ order.businessDate }} · {{ order.tableName ?? 'Mang đi' }}</div>
        </div>
      </div>
    </template>
    <Skeleton v-if="loading && !order" height="300px" />
    <div v-else-if="order" class="stack">
      <Message v-if="order.needsAttention" severity="warn" :closable="false">{{ order.attentionNote ?? 'Đơn cần nhân viên kiểm tra.' }}</Message>
      <Message v-if="order.cancelReason" severity="error" :closable="false">Lý do: {{ order.cancelReason }}</Message>

      <div class="row wrap">
        <Button
          v-for="s in forwardActions"
          :key="s"
          :label="actionMeta[s].label"
          :icon="'pi ' + actionMeta[s].icon"
          :severity="actionMeta[s].severity"
          :loading="busy === order.id + s"
          @click="transition(order, s)"
        />
        <Button v-if="canPay" id="btn-record-payment" label="Thanh toán" icon="pi pi-wallet" severity="success" outlined @click="openPay" />
        <div class="spacer" />
        <Button v-for="s in destructiveActions" :key="s" :label="actionMeta[s].label" :icon="'pi ' + actionMeta[s].icon" severity="danger" text @click="askReason(s)" />
      </div>
      <p v-if="!order.allowedNextStatuses.length && !['Completed', 'Cancelled', 'Rejected'].includes(order.status)" class="faint small">
        Bạn không có quyền chuyển trạng thái tiếp theo của đơn này.
      </p>

      <div class="card items">
        <div v-for="it in order.items" :key="it.id" class="item">
          <div class="qty">{{ it.quantity }}×</div>
          <div style="flex: 1">
            <div style="font-weight: 600">{{ it.itemName }} <span class="muted">({{ it.variantName }})</span></div>
            <div v-if="it.addOns.length" class="small muted">+ {{ it.addOns.map((a) => a.name).join(', ') }}</div>
            <div v-if="it.note" class="small" style="color: var(--warning)">“{{ it.note }}”</div>
          </div>
          <MoneyText :value="it.lineTotal" />
        </div>
        <Divider />
        <div class="totals">
          <div><span>Tạm tính</span><MoneyText :value="order.subtotal" /></div>
          <div v-if="order.discountAmount"><span>Giảm giá</span><span>-{{ money(order.discountAmount) }}</span></div>
          <div v-if="order.taxAmount"><span>Thuế ({{ order.taxRatePercent }}%{{ order.pricesIncludeTax ? ', đã gồm' : '' }})</span><MoneyText :value="order.taxAmount" /></div>
          <div v-if="order.roundingAmount"><span>Làm tròn</span><MoneyText :value="order.roundingAmount" /></div>
          <div class="grand"><span>Tổng cộng</span><MoneyText :value="order.totalAmount" strong /></div>
        </div>
      </div>

      <div v-if="order.customerNote" class="card small"><i class="pi pi-comment faint" /> {{ order.customerNote }}</div>

      <div class="card">
        <div class="card-title"><h3>Thanh toán</h3><StatusPill :status="order.paymentStatus" /></div>
        <div v-if="!order.payments.length" class="faint small">Chưa có giao dịch.</div>
        <div v-for="p in order.payments" :key="p.id" class="row small pay-row">
          <span class="pill" :class="p.type === 'Refund' ? 'Refunded' : 'Paid'">{{ p.type === 'Refund' ? 'Hoàn tiền' : methodLabels[p.method] ?? p.method }}</span>
          <MoneyText :value="p.amount" strong />
          <span v-if="p.changeAmount" class="muted">thối {{ money(p.changeAmount) }}</span>
          <span v-if="p.reference" class="mono faint">{{ p.reference }}</span>
          <span class="spacer" />
          <span class="faint">{{ dateTime(p.recordedAtUtc) }}</span>
        </div>
        <div class="row wrap" style="margin-top: 12px">
          <Button label="In K80" icon="pi pi-print" size="small" outlined @click="invoice('K80', true)" />
          <Button label="PDF A5" icon="pi pi-file-pdf" size="small" text @click="invoice('A5', false)" />
          <Button v-if="canRefund" label="Hoàn tiền" icon="pi pi-replay" size="small" text severity="danger" @click="(refund.open = true), (refund.reason = '')" />
        </div>
        <p class="faint small" style="margin: 8px 0 0">Hóa đơn PDF nội bộ — không phải hóa đơn điện tử theo quy định thuế.</p>
      </div>

      <div class="card">
        <h3 style="margin-bottom: 10px">Lịch sử</h3>
        <div class="timeline">
          <div v-for="(h, i) in order.history" :key="i" class="tl">
            <div class="dot" />
            <div>
              <div><strong>{{ statusLabels[h.toStatus] ?? h.toStatus }}</strong> <span class="faint small">· {{ h.actor }}</span></div>
              <div class="faint small">{{ dateTime(h.changedAtUtc) }}<span v-if="h.reason"> · {{ h.reason }}</span></div>
            </div>
          </div>
        </div>
        <div class="faint small" style="margin-top: 8px">Kho: {{ order.inventoryDeducted ? 'đã trừ theo định mức' : 'chưa trừ' }}</div>
      </div>
    </div>

    <Dialog v-model:visible="reasonDialog.open" modal :header="actionMeta[reasonDialog.to]?.label" :style="{ width: '420px' }">
      <p v-if="reasonDialog.to === 'Cancelled' && order?.inventoryDeducted" class="muted small">Nguyên liệu đã trừ sẽ được hoàn lại bằng bút toán đảo.</p>
      <Textarea v-model="reasonDialog.reason" rows="3" placeholder="Lý do (bắt buộc)" class="w-full" />
      <template #footer>
        <Button label="Đóng" text severity="secondary" @click="reasonDialog.open = false" />
        <Button :label="actionMeta[reasonDialog.to]?.label" severity="danger" :disabled="reasonDialog.reason.trim().length < 3" @click="confirmReason" />
      </template>
    </Dialog>

    <Dialog v-model:visible="pay.open" modal header="Ghi nhận thanh toán" :style="{ width: '440px' }">
      <div class="stack">
        <div class="pay-total">
          <span class="muted">Cần thu</span>
          <span class="gradient-text">{{ money(order?.totalAmount) }}</span>
        </div>
        <SelectButton v-model="pay.method" :options="methodOptions" option-label="label" option-value="value" :allow-empty="false" />
        <template v-if="pay.method === 'Cash'">
          <div class="field">
            <label for="pay-tendered">Khách đưa</label>
            <InputNumber input-id="pay-tendered" v-model="pay.tendered" :min="0" locale="vi-VN" suffix=" ₫" />
          </div>
          <div class="row wrap">
            <Button v-for="v in quickCash" :key="v" :label="money(v)" size="small" outlined severity="secondary" @click="pay.tendered = v" />
          </div>
          <div class="row"><span class="muted">Tiền thối</span><span class="spacer" /><strong :style="{ color: change < 0 ? 'var(--danger)' : 'var(--success)' }">{{ money(change) }}</strong></div>
        </template>
        <div v-else class="field">
          <label for="pay-ref">Mã giao dịch / nội dung CK</label>
          <InputText id="pay-ref" v-model="pay.reference" />
        </div>
        <Message severity="info" :closable="false">Ghi nhận nội bộ — hệ thống không kết nối cổng thanh toán.</Message>
      </div>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="pay.open = false" />
        <Button id="btn-confirm-payment" label="Xác nhận đã thu" icon="pi pi-check" severity="success" :loading="pay.saving" :disabled="pay.method === 'Cash' && change < 0" @click="submitPay" />
      </template>
    </Dialog>

    <Dialog v-model:visible="refund.open" modal header="Hoàn tiền" :style="{ width: '420px' }">
      <p class="muted small">Ghi nhận hoàn toàn bộ {{ money(order?.totalAmount) }}. Thao tác được ghi audit.</p>
      <Textarea v-model="refund.reason" rows="3" placeholder="Lý do hoàn tiền" class="w-full" />
      <template #footer>
        <Button label="Đóng" text severity="secondary" @click="refund.open = false" />
        <Button label="Hoàn tiền" severity="danger" :disabled="refund.reason.trim().length < 3" @click="submitRefund" />
      </template>
    </Dialog>
  </Drawer>
</template>

<style scoped>
.num { font-size: 2rem; font-weight: 900; letter-spacing: -0.03em; }
.items { padding: 14px 16px; }
.item { display: flex; gap: 12px; align-items: flex-start; padding: 6px 0; }
.qty { font-weight: 800; color: var(--accent); min-width: 28px; }
.totals { display: grid; gap: 4px; }
.totals > div { display: flex; justify-content: space-between; color: var(--text-muted); }
.totals .grand { color: var(--text); font-size: 1.1rem; margin-top: 4px; }
.pay-row { padding: 6px 0; border-bottom: 1px dashed var(--border); }
.timeline { display: grid; gap: 10px; }
.tl { display: flex; gap: 12px; }
.dot { width: 10px; height: 10px; border-radius: 50%; background: var(--accent-grad); margin-top: 6px; flex-shrink: 0; }
.pay-total { display: flex; justify-content: space-between; align-items: baseline; font-size: 1.6rem; font-weight: 800; }
.pay-total .muted { font-size: .9rem; font-weight: 500; }
</style>
