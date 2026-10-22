<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { api, ApiError, errorMessage } from '@/api/http'
import type { PublicMenu, PublicMenuItem, PublicOrder } from '@/api/types'
import { recentOrders, rememberOrder, useCartStore } from '@/stores/cart'
import { money } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const cart = useCartStore()
const branchCode = String(route.params.branchCode ?? '').toUpperCase()
const tableCode = typeof route.query.table === 'string' ? route.query.table : null

const menu = ref<PublicMenu | null>(null)
const loading = ref(true)
const loadError = ref('')
const activeCategory = ref<string | null>(null)
const search = ref('')
const recent = ref(recentOrders(branchCode))

const picker = reactive({ open: false, item: null as PublicMenuItem | null, variantId: '', addOnIds: [] as string[], quantity: 1, note: '' })
const cartOpen = ref(false)
const submitting = ref(false)
const checkoutError = ref('')

const categories = computed(() => (menu.value?.categories ?? []).filter((c) => menu.value!.items.some((i) => i.categoryId === c.id)))
const visibleItems = computed(() =>
  (menu.value?.items ?? []).filter(
    (i) => (!activeCategory.value || i.categoryId === activeCategory.value) && (!search.value || i.name.toLowerCase().includes(search.value.toLowerCase())),
  ),
)
const minPrice = (i: PublicMenuItem) => Math.min(...i.variants.map((v) => v.price))
const pickerVariant = computed(() => picker.item?.variants.find((v) => v.id === picker.variantId))
const pickerTotal = computed(() => {
  if (!picker.item || !pickerVariant.value) return 0
  const addOns = picker.item.addOns.filter((a) => picker.addOnIds.includes(a.id)).reduce((s, a) => s + a.price, 0)
  return (pickerVariant.value.price + addOns) * picker.quantity
})

async function loadMenu() {
  loading.value = true
  loadError.value = ''
  try {
    menu.value = await api.get<PublicMenu>(`/public/branches/${encodeURIComponent(branchCode)}/menu`, { table: tableCode }, { anonymous: true })
    document.title = `${menu.value.branch.name} · Gọi món`
    return menu.value
  } catch (e) {
    loadError.value = e instanceof ApiError && e.status === 404 ? 'Không tìm thấy chi nhánh hoặc bàn. Vui lòng quét lại mã QR.' : errorMessage(e)
    return null
  } finally {
    loading.value = false
  }
}

function openPicker(item: PublicMenuItem) {
  Object.assign(picker, { open: true, item, variantId: item.variants[0]?.id ?? '', addOnIds: [], quantity: 1, note: '' })
}

function addToCart() {
  const item = picker.item
  const variant = pickerVariant.value
  if (!item || !variant) return
  const addOns = item.addOns.filter((a) => picker.addOnIds.includes(a.id))
  cart.add({
    menuItemId: item.id,
    variantId: variant.id,
    name: item.name,
    variantName: variant.name,
    unitPrice: variant.price,
    addOnIds: addOns.map((a) => a.id),
    addOnNames: addOns.map((a) => a.name),
    addOnTotal: addOns.reduce((s, a) => s + a.price, 0),
    quantity: picker.quantity,
    note: picker.note.trim(),
  })
  picker.open = false
  toast.add({ severity: 'success', summary: `Đã thêm ${item.name}`, life: 1500 })
}

async function checkout() {
  if (!cart.lines.length || submitting.value) return
  checkoutError.value = ''
  submitting.value = true
  const key = cart.ensureIdempotencyKey()
  try {
    const order = await api.post<PublicOrder>(
      '/public/orders',
      {
        branchCode,
        tableCode,
        channel: tableCode ? 'TableQr' : 'Kiosk',
        customerNote: cart.note || null,
        items: cart.lines.map((l) => ({ menuItemId: l.menuItemId, variantId: l.variantId, quantity: l.quantity, addOnIds: l.addOnIds, note: l.note || null })),
        expectedTotal: cart.total,
      },
      { anonymous: true, headers: { 'Idempotency-Key': key }, timeoutMs: 20000 },
    )
    if (!order.trackingToken) throw new Error('missing token')
    rememberOrder({ token: order.trackingToken, orderNumber: order.orderNumber, branchCode, createdAt: new Date().toISOString() })
    cart.clear()
    cartOpen.value = false
    router.push({ name: 'tracking', params: { token: order.trackingToken } })
  } catch (e) {
    if (e instanceof ApiError && (e.code === 'price_changed' || e.code === 'items_unavailable')) {
      const fresh = await loadMenu()
      if (fresh) cart.reprice(fresh)
      checkoutError.value =
        e.code === 'price_changed'
          ? 'Giá một số món vừa thay đổi. Giỏ hàng đã được cập nhật theo giá mới — vui lòng kiểm tra và đặt lại.'
          : 'Một số món không còn phục vụ và đã được bỏ khỏi giỏ. Vui lòng kiểm tra lại.'
    } else if (e instanceof ApiError && e.status === 429) {
      checkoutError.value = 'Bạn thao tác quá nhanh. Vui lòng đợi vài giây rồi thử lại.'
    } else {
      // Network/timeouts: keep the same Idempotency-Key so retrying cannot create a duplicate order.
      checkoutError.value = errorMessage(e) + ' Bạn có thể bấm "Đặt món" lại an toàn — đơn sẽ không bị tạo trùng.'
    }
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  cart.init(branchCode, tableCode)
  const m = await loadMenu()
  if (m && cart.lines.length && cart.reprice(m)) toast.add({ severity: 'info', summary: 'Giỏ hàng đã cập nhật theo menu mới nhất', life: 3000 })
})
</script>

<template>
  <div class="cust">
    <div v-if="loading && !menu" class="center"><ProgressSpinner /></div>
    <div v-else-if="loadError" class="center">
      <div class="card" style="text-align: center; max-width: 380px">
        <i class="pi pi-exclamation-circle" style="font-size: 2rem; color: var(--warning)" />
        <h1 style="font-size: 1.2rem; margin: 10px 0">Không mở được menu</h1>
        <p class="muted">{{ loadError }}</p>
        <Button label="Thử lại" icon="pi pi-refresh" @click="loadMenu" />
      </div>
    </div>
    <template v-else-if="menu">
      <header class="hero">
        <div class="hero-bg" />
        <div class="hero-content">
          <div class="pill Ready" v-if="menu.branch.orderingEnabled">Đang nhận đơn</div>
          <div class="pill Cancelled" v-else>Tạm ngưng nhận đơn</div>
          <h1>{{ menu.branch.name }}</h1>
          <p class="muted"><i class="pi pi-map-marker" /> {{ menu.branch.address }}</p>
          <div class="table-chip"><i class="pi pi-table" /> {{ menu.branch.tableName ?? 'Mang đi' }}</div>
        </div>
      </header>

      <div v-if="recent.length" class="recent">
        <span class="muted small">Đơn gần đây:</span>
        <RouterLink v-for="r in recent" :key="r.token" :to="{ name: 'tracking', params: { token: r.token } }" class="pill Confirmed">#{{ r.orderNumber }}</RouterLink>
      </div>

      <div class="sticky-bar">
        <span class="p-input-icon-left search">
          <InputText v-model="search" placeholder="Tìm món…" class="w-full" aria-label="Tìm món" />
        </span>
        <div class="cats">
          <button class="cat" :class="{ active: !activeCategory }" @click="activeCategory = null">Tất cả</button>
          <button v-for="c in categories" :key="c.id" class="cat" :class="{ active: activeCategory === c.id }" @click="activeCategory = c.id">{{ c.name }}</button>
        </div>
      </div>

      <main class="items">
        <EmptyState v-if="!visibleItems.length" icon="pi-search" title="Không tìm thấy món" />
        <article v-for="i in visibleItems" :key="i.id" class="item pop-in" @click="menu.branch.orderingEnabled && openPicker(i)">
          <div class="img">
            <img v-if="i.imageUrl" :src="i.imageUrl" :alt="i.name" loading="lazy" />
            <i v-else class="pi pi-star-fill" />
          </div>
          <div class="info">
            <h3>{{ i.name }}</h3>
            <p v-if="i.description" class="small muted desc">{{ i.description }}</p>
            <div class="row">
              <strong class="price">{{ i.variants.length > 1 ? 'từ ' : '' }}{{ money(minPrice(i)) }}</strong>
              <span class="spacer" />
              <Button icon="pi pi-plus" rounded size="small" :disabled="!menu.branch.orderingEnabled" :aria-label="`Thêm ${i.name}`" />
            </div>
          </div>
        </article>
      </main>

      <Transition name="slide-up">
        <button v-if="cart.count" id="btn-open-cart" class="cart-bar" @click="cartOpen = true">
          <span class="cart-count">{{ cart.count }}</span>
          <span>Xem giỏ hàng</span>
          <span class="spacer" />
          <strong>{{ money(cart.total) }}</strong>
          <i class="pi pi-arrow-right" />
        </button>
      </Transition>
    </template>

    <Dialog v-model:visible="picker.open" modal :header="picker.item?.name" :style="{ width: '440px' }" :breakpoints="{ '520px': '96vw' }" position="bottom">
      <div v-if="picker.item" class="stack">
        <p v-if="picker.item.description" class="muted small" style="margin: 0">{{ picker.item.description }}</p>
        <div>
          <div class="label">Chọn size</div>
          <div class="opts">
            <button v-for="v in picker.item.variants" :key="v.id" class="opt" :class="{ active: picker.variantId === v.id }" @click="picker.variantId = v.id">
              <span>{{ v.name }}</span><strong>{{ money(v.price) }}</strong>
            </button>
          </div>
        </div>
        <div v-if="picker.item.addOns.length">
          <div class="label">Topping</div>
          <label v-for="a in picker.item.addOns" :key="a.id" class="addon">
            <Checkbox v-model="picker.addOnIds" :value="a.id" />
            <span>{{ a.name }}</span>
            <span class="spacer" />
            <span class="muted">+{{ money(a.price) }}</span>
          </label>
        </div>
        <div class="field">
          <label for="pk-note">Ghi chú</label>
          <InputText id="pk-note" v-model="picker.note" placeholder="Ít đá, ít ngọt…" maxlength="200" />
        </div>
        <div class="row">
          <div class="qty">
            <Button icon="pi pi-minus" rounded text :disabled="picker.quantity <= 1" @click="picker.quantity--" aria-label="Giảm" />
            <strong>{{ picker.quantity }}</strong>
            <Button icon="pi pi-plus" rounded text :disabled="picker.quantity >= 20" @click="picker.quantity++" aria-label="Tăng" />
          </div>
          <span class="spacer" />
          <Button id="btn-add-to-cart" :label="`Thêm · ${money(pickerTotal)}`" icon="pi pi-shopping-bag" @click="addToCart" />
        </div>
      </div>
    </Dialog>

    <Drawer v-model:visible="cartOpen" position="bottom" header="Giỏ hàng" style="height: auto; max-height: 88vh">
      <div class="cart stack">
        <div v-for="l in cart.lines" :key="l.key" class="cart-line">
          <div style="flex: 1; min-width: 0">
            <div style="font-weight: 600">{{ l.name }} <span class="muted">({{ l.variantName }})</span></div>
            <div v-if="l.addOnNames.length" class="small muted">+ {{ l.addOnNames.join(', ') }}</div>
            <div v-if="l.note" class="small" style="color: var(--warning)">“{{ l.note }}”</div>
            <div class="small">{{ money(l.unitPrice + l.addOnTotal) }}</div>
          </div>
          <div class="qty">
            <Button icon="pi pi-minus" rounded text size="small" @click="cart.setQty(l.key, l.quantity - 1)" aria-label="Giảm" />
            <strong>{{ l.quantity }}</strong>
            <Button icon="pi pi-plus" rounded text size="small" @click="cart.setQty(l.key, l.quantity + 1)" aria-label="Tăng" />
          </div>
        </div>
        <EmptyState v-if="!cart.lines.length" icon="pi-shopping-bag" title="Giỏ hàng trống" />
        <Textarea v-model="cart.note" rows="2" placeholder="Ghi chú cho quán (tùy chọn)" maxlength="300" />
        <Message v-if="checkoutError" severity="warn" :closable="false">{{ checkoutError }}</Message>
        <div class="row total"><span>Tạm tính</span><span class="spacer" /><strong class="gradient-text">{{ money(cart.total) }}</strong></div>
        <p class="faint small" style="margin: 0">Giá cuối cùng do hệ thống xác nhận. Thanh toán tại quầy (tiền mặt hoặc chuyển khoản).</p>
        <Button id="btn-checkout" label="Đặt món" icon="pi pi-send" size="large" :loading="submitting" :disabled="!cart.lines.length" @click="checkout" />
      </div>
    </Drawer>
  </div>
</template>

<style scoped>
.cust { max-width: 720px; margin: 0 auto; width: 100%; padding-bottom: 100px; }
.center { min-height: 70vh; display: grid; place-items: center; padding: 20px; }
.hero { position: relative; overflow: hidden; padding: 40px 20px 26px; }
.hero-bg { position: absolute; inset: 0; background: radial-gradient(600px 300px at 80% 0%, rgba(255, 138, 61, .35), transparent 70%), radial-gradient(400px 260px at 0% 100%, rgba(226, 85, 122, .25), transparent 70%); }
.hero-content { position: relative; }
.hero h1 { font-size: 2rem; margin: 12px 0 4px; }
.table-chip { display: inline-flex; gap: 8px; align-items: center; margin-top: 10px; padding: 6px 14px; border-radius: 999px; background: var(--accent-grad); color: #1b120d; font-weight: 700; }
.recent { display: flex; gap: 8px; align-items: center; padding: 0 20px 10px; flex-wrap: wrap; }
.sticky-bar { position: sticky; top: 0; z-index: 10; padding: 10px 16px; background: rgba(18, 13, 10, .85); backdrop-filter: blur(14px); border-bottom: 1px solid var(--border); }
.cats { display: flex; gap: 8px; overflow-x: auto; margin-top: 10px; scrollbar-width: none; }
.cat { flex-shrink: 0; padding: 7px 14px; border-radius: 999px; border: 1px solid var(--border); background: transparent; color: var(--text-muted); font: inherit; cursor: pointer; transition: all .15s; }
.cat.active { background: var(--accent-soft); color: var(--text); border-color: rgba(255, 179, 92, .4); }
.items { padding: 16px; display: grid; gap: 12px; }
.item { display: flex; gap: 14px; padding: 12px; border-radius: 18px; background: var(--surface); border: 1px solid var(--border); cursor: pointer; transition: transform .15s, border-color .15s; }
.item:active { transform: scale(.98); }
.item:hover { border-color: var(--border-strong); }
.img { width: 92px; height: 92px; border-radius: 14px; overflow: hidden; flex-shrink: 0; display: grid; place-items: center; background: linear-gradient(135deg, rgba(255, 179, 92, .25), rgba(226, 85, 122, .2)); color: var(--accent); font-size: 1.6rem; }
.img img { width: 100%; height: 100%; object-fit: cover; }
.info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
.info h3 { font-size: 1.02rem; }
.desc { margin: 0; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.price { color: var(--accent); margin-top: auto; }
.cart-bar { position: fixed; left: 50%; transform: translateX(-50%); bottom: 18px; width: min(680px, calc(100% - 28px)); display: flex; align-items: center; gap: 12px; padding: 14px 18px; border-radius: 18px; border: 0; background: var(--accent-grad); color: #1b120d; font: inherit; font-weight: 700; cursor: pointer; box-shadow: 0 16px 40px -10px rgba(255, 122, 69, .6); z-index: 20; }
.cart-count { background: #1b120d; color: var(--accent); border-radius: 10px; padding: 2px 10px; }
.slide-up-enter-active, .slide-up-leave-active { transition: all .25s ease; }
.slide-up-enter-from, .slide-up-leave-to { opacity: 0; transform: translate(-50%, 30px); }
.label { font-size: .82rem; color: var(--text-muted); margin-bottom: 8px; }
.opts { display: grid; grid-template-columns: repeat(auto-fill, minmax(110px, 1fr)); gap: 8px; }
.opt { display: flex; flex-direction: column; gap: 2px; padding: 10px; border-radius: 12px; border: 1px solid var(--border); background: transparent; color: var(--text); font: inherit; cursor: pointer; }
.opt.active { border-color: var(--accent); background: var(--accent-soft); }
.addon { display: flex; align-items: center; gap: 10px; padding: 8px 0; cursor: pointer; }
.qty { display: flex; align-items: center; gap: 6px; }
.qty strong { min-width: 22px; text-align: center; }
.cart { max-width: 720px; margin: 0 auto; }
.cart-line { display: flex; gap: 10px; align-items: center; padding: 8px 0; border-bottom: 1px dashed var(--border); }
.total { font-size: 1.2rem; }
</style>
