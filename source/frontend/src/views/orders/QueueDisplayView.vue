<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api, errorMessage } from '@/api/http'
import { useOrdersHub } from '@/realtime/useOrdersHub'
import { chime } from '@/composables/useOrderActions'

interface QueueEntry { orderNumber: number; status: string }
interface Queue { branchCode: string; branchName: string; businessDate: string; orders: QueueEntry[] }

const route = useRoute()
const code = String(route.params.branchCode ?? '')
const queue = ref<Queue | null>(null)
const error = ref('')
const clock = ref(new Date())
const flash = ref<number | null>(null)
const tick = setInterval(() => (clock.value = new Date()), 1000)
onBeforeUnmount(() => clearInterval(tick))

const preparing = computed(() => (queue.value?.orders ?? []).filter((o) => ['Submitted', 'PendingConfirmation', 'Confirmed', 'Preparing'].includes(o.status)))
const ready = computed(() => (queue.value?.orders ?? []).filter((o) => o.status === 'Ready'))

async function load() {
  try {
    queue.value = await api.get<Queue>(`/public/branches/${encodeURIComponent(code)}/queue`, undefined, { anonymous: true })
    error.value = ''
  } catch (e) {
    error.value = errorMessage(e)
  }
}

function apply(e: { orderNumber: number; businessDate: string; status: string }) {
  if (!queue.value || e.businessDate !== queue.value.businessDate) {
    load()
    return
  }
  const list = queue.value.orders.filter((o) => o.orderNumber !== e.orderNumber)
  if (['Submitted', 'PendingConfirmation', 'Confirmed', 'Preparing', 'Ready'].includes(e.status)) list.push({ orderNumber: e.orderNumber, status: e.status })
  list.sort((a, b) => a.orderNumber - b.orderNumber)
  queue.value.orders = list
  if (e.status === 'Ready') {
    flash.value = e.orderNumber
    chime()
    setTimeout(() => flash.value === e.orderNumber && (flash.value = null), 8000)
  }
}

const hub = useOrdersHub({
  authenticated: false,
  onJoin: async (conn) => {
    await conn.invoke('JoinQueueDisplay', code)
  },
  onResync: load,
  handlers: { queueChanged: apply },
})

// Safety net: refresh the snapshot periodically in case events were missed.
const poll = setInterval(load, 60000)
onBeforeUnmount(() => clearInterval(poll))

onMounted(() => {
  load()
  hub.start()
})
</script>

<template>
  <div class="queue">
    <header>
      <div class="brand"><i class="pi pi-sparkles" /> {{ queue?.branchName ?? 'Brewline' }}</div>
      <div class="spacer" />
      <div class="clock">{{ clock.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' }) }}</div>
      <span class="dot" :class="hub.state.value" />
    </header>

    <div v-if="error" class="err">{{ error }}</div>

    <main>
      <section class="panel prep">
        <h2>Đang chuẩn bị</h2>
        <TransitionGroup name="num" tag="div" class="nums">
          <div v-for="o in preparing" :key="o.orderNumber" class="n">{{ o.orderNumber }}</div>
        </TransitionGroup>
        <div v-if="!preparing.length" class="none">—</div>
      </section>
      <section class="panel ready">
        <h2>Mời nhận món</h2>
        <TransitionGroup name="num" tag="div" class="nums">
          <div v-for="o in ready" :key="o.orderNumber" class="n" :class="{ flash: flash === o.orderNumber }">{{ o.orderNumber }}</div>
        </TransitionGroup>
        <div v-if="!ready.length" class="none">—</div>
      </section>
    </main>
  </div>
</template>

<style scoped>
.queue { min-height: 100vh; display: flex; flex-direction: column; padding: 28px 40px; gap: 24px; }
header { display: flex; align-items: center; gap: 16px; }
.brand { font-size: 1.6rem; font-weight: 800; display: flex; gap: 12px; align-items: center; }
.brand .pi { background: var(--accent-grad); color: #1b120d; padding: 10px; border-radius: 12px; }
.clock { font-size: 2rem; font-weight: 700; font-variant-numeric: tabular-nums; }
.dot { width: 12px; height: 12px; border-radius: 50%; background: var(--danger); }
.dot.connected { background: var(--success); }
.err { color: var(--danger); text-align: center; }
main { flex: 1; display: grid; grid-template-columns: 1fr 1.3fr; gap: 24px; }
.panel { border-radius: 28px; padding: 28px; border: 1px solid var(--border); background: var(--surface); }
.panel h2 { font-size: 1.8rem; margin-bottom: 24px; text-transform: uppercase; letter-spacing: .08em; }
.prep h2 { color: var(--text-muted); }
.ready { background: linear-gradient(160deg, rgba(79, 209, 161, .12), rgba(18, 13, 10, .6)); border-color: rgba(79, 209, 161, .3); }
.ready h2 { color: var(--success); }
.nums { display: flex; flex-wrap: wrap; gap: 16px; }
.n { min-width: 120px; padding: 16px 20px; text-align: center; font-size: 3.2rem; font-weight: 900; border-radius: 20px; background: rgba(255, 255, 255, .04); font-variant-numeric: tabular-nums; }
.ready .n { font-size: 4.4rem; background: rgba(79, 209, 161, .14); color: #c9f7e4; }
.n.flash { animation: pop 1s ease infinite alternate; box-shadow: 0 0 0 3px var(--success), 0 0 60px rgba(79, 209, 161, .45); }
.none { color: var(--text-faint); font-size: 2rem; }
@keyframes pop { from { transform: scale(1); } to { transform: scale(1.08); } }
.num-enter-active, .num-leave-active { transition: all .4s ease; }
.num-enter-from { opacity: 0; transform: scale(.6); }
.num-leave-to { opacity: 0; transform: translateY(-20px); }
@media (max-width: 900px) { main { grid-template-columns: 1fr; } .queue { padding: 16px; } }
</style>
