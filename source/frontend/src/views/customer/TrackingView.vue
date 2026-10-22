<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { api, ApiError, errorMessage } from '@/api/http'
import type { PublicOrder } from '@/api/types'
import { useOrdersHub } from '@/realtime/useOrdersHub'
import { chime } from '@/composables/useOrderActions'
import { money, statusLabels, timeOnly } from '@/utils/format'

const route = useRoute()
const toast = useToast()
const token = String(route.params.token ?? '')
const order = ref<PublicOrder | null>(null)
const loading = ref(true)
const error = ref('')
const cancel = reactive({ open: false, reason: '', busy: false })

const steps = [
  { key: 'Submitted', label: 'Đã gửi', icon: 'pi-send' },
  { key: 'Confirmed', label: 'Đã xác nhận', icon: 'pi-check' },
  { key: 'Preparing', label: 'Đang pha chế', icon: 'pi-cog' },
  { key: 'Ready', label: 'Mời nhận món', icon: 'pi-bell' },
  { key: 'Completed', label: 'Hoàn tất', icon: 'pi-check-circle' },
]
const rank: Record<string, number> = { Submitted: 0, PendingConfirmation: 0, Confirmed: 1, Preparing: 2, Ready: 3, Completed: 4 }
const currentRank = computed(() => rank[order.value?.status ?? ''] ?? -1)
const terminalBad = computed(() => order.value && ['Cancelled', 'Rejected'].includes(order.value.status))
const stepTime = (key: string) => {
  const t = order.value?.timeline.find((x) => x.status === key || (key === 'Submitted' && x.status === 'PendingConfirmation'))
  return t ? timeOnly(t.atUtc) : ''
}

async function load() {
  try {
    order.value = await api.get<PublicOrder>(`/public/orders/${encodeURIComponent(token)}`, undefined, { anonymous: true })
    error.value = ''
  } catch (e) {
    error.value = e instanceof ApiError && e.status === 404 ? 'Không tìm thấy đơn. Liên kết có thể không đúng.' : errorMessage(e)
  } finally {
    loading.value = false
  }
}

const hub = useOrdersHub({
  authenticated: false,
  onJoin: async (conn) => {
    await conn.invoke('JoinOrderTracking', token)
  },
  onResync: load,
  handlers: {
    orderStatusChanged: async (e: { status: string }) => {
      await load()
      if (e.status === 'Ready') {
        chime()
        navigator.vibrate?.([200, 100, 200])
        toast.add({ severity: 'success', summary: 'Món của bạn đã sẵn sàng!', detail: 'Mời bạn đến quầy nhận món.', life: 8000 })
      }
    },
    paymentChanged: load,
  },
})

async function doCancel() {
  cancel.busy = true
  try {
    order.value = await api.post<PublicOrder>(`/public/orders/${encodeURIComponent(token)}/cancel`, { reason: cancel.reason || null }, { anonymous: true })
    cancel.open = false
    toast.add({ severity: 'info', summary: 'Đã hủy đơn', life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không hủy được', detail: errorMessage(e), life: 5000 })
    load()
  } finally {
    cancel.busy = false
  }
}

onMounted(async () => {
  await load()
  hub.start()
})
</script>

<template>
  <div class="track">
    <div v-if="loading" class="center"><ProgressSpinner /></div>
    <div v-else-if="error && !order" class="center">
      <div class="card" style="text-align: center; max-width: 380px">
        <i class="pi pi-search" style="font-size: 2rem; color: var(--text-faint)" />
        <h1 style="font-size: 1.2rem; margin: 10px 0">{{ error }}</h1>
      </div>
    </div>
    <template v-else-if="order">
      <section class="hero card pop-in" :class="{ ready: order.status === 'Ready', bad: terminalBad }">
        <div class="muted small">{{ order.branchName }} · {{ order.tableName ?? 'Mang đi' }}</div>
        <div class="label">Số đơn của bạn</div>
        <h1 class="num">{{ order.orderNumber }}</h1>
        <StatusPill :status="order.status" />
        <p v-if="order.status === 'Ready'" class="big">Mời bạn đến quầy nhận món 🎉</p>
        <p v-else-if="terminalBad" class="muted">Đơn đã {{ statusLabels[order.status]?.toLowerCase() }}. Vui lòng liên hệ quầy nếu cần hỗ trợ.</p>
        <p v-else class="muted small">Trang tự cập nhật — bạn không cần tải lại. <span class="live" :class="hub.state.value" /></p>
      </section>

      <section v-if="!terminalBad" class="card steps">
        <div v-for="(s, i) in steps" :key="s.key" class="step" :class="{ done: currentRank >= i, current: currentRank === i }">
          <div class="icon"><i class="pi" :class="s.icon" /></div>
          <div class="txt">
            <div>{{ s.label }}</div>
            <div class="faint small">{{ stepTime(s.key) }}</div>
          </div>
        </div>
      </section>

      <section class="card">
        <div class="card-title"><h2>Chi tiết đơn</h2><StatusPill :status="order.paymentStatus" /></div>
        <div v-for="(it, idx) in order.items" :key="idx" class="line">
          <strong class="q">{{ it.quantity }}×</strong>
          <div style="flex: 1">
            <div>{{ it.name }} <span class="muted">({{ it.variant }})</span></div>
            <div v-if="it.addOns.length" class="small muted">+ {{ it.addOns.join(', ') }}</div>
            <div v-if="it.note" class="small" style="color: var(--warning)">“{{ it.note }}”</div>
          </div>
          <span>{{ money(it.lineTotal) }}</span>
        </div>
        <Divider />
        <div class="row"><span class="muted">Tạm tính</span><span class="spacer" />{{ money(order.subtotal) }}</div>
        <div v-if="order.taxAmount" class="row"><span class="muted">Thuế</span><span class="spacer" />{{ money(order.taxAmount) }}</div>
        <div class="row total"><span>Tổng cộng</span><span class="spacer" /><strong class="gradient-text">{{ money(order.totalAmount) }}</strong></div>
        <p class="faint small">Thanh toán tại quầy bằng tiền mặt hoặc chuyển khoản.</p>
        <Button v-if="order.canCancel" id="btn-cancel-order" label="Hủy đơn" icon="pi pi-times" severity="danger" text @click="cancel.open = true" />
      </section>

      <RouterLink :to="{ name: 'customer-menu', params: { branchCode: order.branchCode } }" class="back"><i class="pi pi-arrow-left" /> Gọi thêm món</RouterLink>
    </template>

    <Dialog v-model:visible="cancel.open" modal header="Hủy đơn?" :style="{ width: '400px' }" :breakpoints="{ '480px': '94vw' }">
      <p class="muted small">Chỉ hủy được khi quán chưa xác nhận đơn.</p>
      <Textarea v-model="cancel.reason" rows="2" placeholder="Lý do (tùy chọn)" class="w-full" maxlength="200" />
      <template #footer>
        <Button label="Không" text severity="secondary" @click="cancel.open = false" />
        <Button label="Hủy đơn" severity="danger" :loading="cancel.busy" @click="doCancel" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.track { max-width: 560px; margin: 0 auto; width: 100%; padding: 24px 16px; display: flex; flex-direction: column; gap: 16px; }
.center { min-height: 70vh; display: grid; place-items: center; }
.hero { text-align: center; padding: 28px 20px; position: relative; overflow: hidden; }
.hero::before { content: ''; position: absolute; inset: -40% -20% auto; height: 220px; background: radial-gradient(closest-side, rgba(255, 138, 61, .3), transparent); }
.hero.ready::before { background: radial-gradient(closest-side, rgba(79, 209, 161, .4), transparent); }
.hero.bad::before { background: radial-gradient(closest-side, rgba(255, 107, 107, .3), transparent); }
.hero > * { position: relative; }
.label { margin-top: 10px; color: var(--text-muted); text-transform: uppercase; letter-spacing: .1em; font-size: .78rem; }
.num { font-size: 5rem; line-height: 1; font-weight: 900; margin: 6px 0 12px; background: var(--accent-grad); -webkit-background-clip: text; background-clip: text; color: transparent; }
.hero.ready .num { background: linear-gradient(135deg, #4fd1a1, #7cc4ff); -webkit-background-clip: text; background-clip: text; animation: pop-in .5s ease; }
.big { font-size: 1.15rem; font-weight: 700; color: var(--success); }
.steps { display: grid; gap: 4px; }
.step { display: flex; gap: 14px; align-items: center; padding: 8px 0; opacity: .45; transition: opacity .3s; }
.step.done { opacity: 1; }
.step .icon { width: 38px; height: 38px; border-radius: 50%; display: grid; place-items: center; border: 1px solid var(--border-strong); }
.step.done .icon { background: var(--accent-soft); color: var(--accent); border-color: rgba(255, 179, 92, .4); }
.step.current .icon { background: var(--accent-grad); color: #1b120d; animation: pulse-ring 1.6s ease-out infinite; }
.line { display: flex; gap: 10px; padding: 6px 0; }
.q { color: var(--accent); min-width: 26px; }
.total { font-size: 1.15rem; margin-top: 6px; }
.back { text-align: center; padding: 8px; }
.live { width: 8px; height: 8px; border-radius: 50%; display: inline-block; background: var(--warning); margin-left: 4px; }
.live.connected { background: var(--success); }
</style>
