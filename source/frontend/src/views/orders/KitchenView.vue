<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { OrderSummary, OrderStatus, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { useOrdersHub } from '@/realtime/useOrdersHub'
import { chime, useOrderActions } from '@/composables/useOrderActions'
import OrderDetailDrawer from '@/components/OrderDetailDrawer.vue'
import { elapsed } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const orders = ref<OrderSummary[]>([])
const loading = ref(true)
const selected = ref<string | null>(null)
const sound = ref(true)
const now = ref(Date.now())
const ticker = setInterval(() => (now.value = Date.now()), 15000)
onBeforeUnmount(() => clearInterval(ticker))

const columns: { key: string; title: string; statuses: OrderStatus[]; next?: OrderStatus; nextLabel?: string; icon: string }[] = [
  { key: 'new', title: 'Chờ xác nhận', statuses: ['Submitted', 'PendingConfirmation'], next: 'Confirmed', nextLabel: 'Xác nhận', icon: 'pi-inbox' },
  { key: 'confirmed', title: 'Đã xác nhận', statuses: ['Confirmed'], next: 'Preparing', nextLabel: 'Bắt đầu pha', icon: 'pi-check' },
  { key: 'preparing', title: 'Đang pha chế', statuses: ['Preparing'], next: 'Ready', nextLabel: 'Sẵn sàng', icon: 'pi-cog' },
  { key: 'ready', title: 'Chờ giao', statuses: ['Ready'], next: 'Completed', nextLabel: 'Đã giao', icon: 'pi-bell' },
]
const byColumn = computed(() =>
  Object.fromEntries(columns.map((c) => [c.key, orders.value.filter((o) => c.statuses.includes(o.status)).sort((a, b) => a.orderNumber - b.orderNumber)])),
)
const ageClass = (o: OrderSummary) => {
  const mins = (now.value - new Date(o.createdAtUtc).getTime()) / 60000
  return mins > 15 ? 'late' : mins > 8 ? 'warn' : ''
}

async function load() {
  if (!auth.currentBranch) return
  try {
    const res = await api.get<Paged<OrderSummary>>(`/branches/${auth.currentBranch.id}/orders`, { activeOnly: true, pageSize: 200 })
    orders.value = res.items
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}
let t: ReturnType<typeof setTimeout> | undefined
const scheduleReload = () => {
  clearTimeout(t)
  t = setTimeout(load, 200)
}

const { busy, transition } = useOrderActions(scheduleReload)

const hub = useOrdersHub({
  onJoin: async (conn) => {
    if (auth.currentBranch) await conn.invoke('JoinBranchStaff', auth.currentBranch.id)
  },
  onResync: load,
  handlers: {
    orderCreated: (e: { orderNumber: number }) => {
      if (sound.value) chime()
      toast.add({ severity: 'info', summary: `Đơn mới #${e.orderNumber}`, life: 4000 })
      scheduleReload()
    },
    orderStatusChanged: scheduleReload,
    paymentChanged: scheduleReload,
  },
})

async function toggleFullscreen() {
  if (document.fullscreenElement) await document.exitFullscreen()
  else await document.documentElement.requestFullscreen().catch(() => undefined)
}

onMounted(() => {
  load()
  hub.start()
})
</script>

<template>
  <div class="page kitchen">
    <div class="page-header">
      <div>
        <h1>Bảng pha chế</h1>
        <p>{{ auth.currentBranch?.name }} · {{ orders.length }} đơn đang xử lý · realtime: {{ hub.state.value === 'connected' ? 'đã kết nối' : 'đang kết nối…' }}</p>
      </div>
      <div class="toolbar">
        <div class="row"><ToggleSwitch v-model="sound" input-id="k-sound" /><label for="k-sound" class="small">Âm báo</label></div>
        <Button icon="pi pi-window-maximize" text rounded v-tooltip.bottom="'Toàn màn hình'" @click="toggleFullscreen" />
        <Button icon="pi pi-refresh" text rounded @click="load" />
      </div>
    </div>

    <div class="board">
      <section v-for="c in columns" :key="c.key" class="col" :class="c.key">
        <header>
          <i class="pi" :class="c.icon" />
          <span>{{ c.title }}</span>
          <Badge :value="byColumn[c.key].length" />
        </header>
        <div class="col-body">
          <Skeleton v-if="loading" height="120px" />
          <TransitionGroup name="ticket">
            <article v-for="o in byColumn[c.key]" :key="o.id" class="ticket" :class="[ageClass(o), { attention: o.needsAttention }]" @click="selected = o.id">
              <div class="t-head">
                <span class="t-num">#{{ o.orderNumber }}</span>
                <span class="t-table">{{ o.tableName ?? 'Mang đi' }}</span>
                <span class="spacer" />
                <span class="t-age"><i class="pi pi-clock" /> {{ elapsed(o.createdAtUtc, now) }}</span>
              </div>
              <ul class="t-items">
                <li v-for="i in o.items" :key="i.id">
                  <strong>{{ i.quantity }}×</strong> {{ i.itemName }} <span class="muted">{{ i.variantName }}</span>
                  <div v-if="i.addOns.length" class="small muted">+ {{ i.addOns.map((a) => a.name).join(', ') }}</div>
                  <div v-if="i.note" class="small note">“{{ i.note }}”</div>
                </li>
              </ul>
              <div v-if="o.customerNote" class="small note"><i class="pi pi-comment" /> {{ o.customerNote }}</div>
              <div class="t-foot">
                <StatusPill :status="o.paymentStatus" />
                <span class="spacer" />
                <Button
                  v-if="c.next"
                  :label="c.nextLabel"
                  size="small"
                  :severity="c.key === 'preparing' || c.key === 'ready' ? 'success' : undefined"
                  :loading="busy === o.id + c.next"
                  @click.stop="transition(o, c.next!)"
                />
              </div>
            </article>
          </TransitionGroup>
          <div v-if="!loading && !byColumn[c.key].length" class="col-empty">Trống</div>
        </div>
      </section>
    </div>

    <OrderDetailDrawer :order-id="selected" @close="selected = null" @changed="scheduleReload" />
  </div>
</template>

<style scoped>
.kitchen { max-width: none; }
.board { display: grid; grid-template-columns: repeat(4, minmax(260px, 1fr)); gap: 14px; overflow-x: auto; }
.col { background: rgba(255, 255, 255, .02); border: 1px solid var(--border); border-radius: var(--radius); display: flex; flex-direction: column; min-height: calc(100vh - 220px); }
.col header { display: flex; align-items: center; gap: 10px; padding: 14px 16px; font-weight: 700; border-bottom: 1px solid var(--border); }
.col.new header .pi { color: var(--warning); }
.col.confirmed header .pi { color: var(--info); }
.col.preparing header .pi { color: #c49bff; }
.col.ready header .pi { color: var(--success); }
.col-body { padding: 12px; display: flex; flex-direction: column; gap: 10px; flex: 1; }
.col-empty { text-align: center; color: var(--text-faint); padding: 30px 0; font-size: .85rem; }
.ticket { background: var(--surface-strong); border: 1px solid var(--border); border-radius: 14px; padding: 12px 14px; cursor: pointer; transition: transform .15s, border-color .15s, box-shadow .15s; }
.ticket:hover { transform: translateY(-2px); border-color: var(--border-strong); box-shadow: var(--shadow); }
.ticket.warn { border-left: 3px solid var(--warning); }
.ticket.late { border-left: 3px solid var(--danger); }
.ticket.attention { box-shadow: 0 0 0 1px var(--warning); }
.t-head { display: flex; align-items: center; gap: 8px; }
.t-num { font-size: 1.35rem; font-weight: 900; letter-spacing: -0.02em; }
.t-table { font-size: .8rem; padding: 2px 8px; border-radius: 8px; background: var(--accent-soft); color: var(--accent); }
.t-age { font-size: .78rem; color: var(--text-faint); }
.ticket.late .t-age { color: var(--danger); font-weight: 700; }
.t-items { list-style: none; padding: 0; margin: 10px 0; display: grid; gap: 6px; }
.note { color: var(--warning); }
.t-foot { display: flex; align-items: center; gap: 8px; margin-top: 8px; }
.ticket-enter-active, .ticket-leave-active { transition: all .3s ease; }
.ticket-enter-from { opacity: 0; transform: translateY(-12px) scale(.97); }
.ticket-leave-to { opacity: 0; transform: translateX(30px); }
</style>
