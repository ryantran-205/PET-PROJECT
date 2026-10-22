import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import type { PublicMenu } from '@/api/types'

export interface CartLine {
  key: string
  menuItemId: string
  variantId: string
  name: string
  variantName: string
  unitPrice: number
  addOnIds: string[]
  addOnNames: string[]
  addOnTotal: number
  quantity: number
  note: string
}

const storageKey = (branch: string) => `brewline.cart.${branch}`

/**
 * Customer cart (per branch, persisted to localStorage). Prices here are display-only;
 * the server recomputes everything and rejects with `price_changed` if `expectedTotal` differs.
 */
export const useCartStore = defineStore('cart', () => {
  const branchCode = ref('')
  const tableCode = ref<string | null>(null)
  const lines = ref<CartLine[]>([])
  const note = ref('')
  /** Reused across retries of the same checkout so a double-submit never creates two orders. */
  const idempotencyKey = ref<string | null>(null)

  const count = computed(() => lines.value.reduce((a, l) => a + l.quantity, 0))
  const total = computed(() => lines.value.reduce((a, l) => a + (l.unitPrice + l.addOnTotal) * l.quantity, 0))

  function init(branch: string, table: string | null) {
    branchCode.value = branch.toUpperCase()
    tableCode.value = table
    try {
      const saved = JSON.parse(localStorage.getItem(storageKey(branchCode.value)) ?? 'null')
      lines.value = Array.isArray(saved?.lines) ? saved.lines : []
      note.value = saved?.note ?? ''
    } catch {
      lines.value = []
    }
    idempotencyKey.value = null
  }

  watch([lines, note], () => {
    if (branchCode.value) localStorage.setItem(storageKey(branchCode.value), JSON.stringify({ lines: lines.value, note: note.value }))
    idempotencyKey.value = null
  }, { deep: true })

  function add(line: Omit<CartLine, 'key'>) {
    const key = [line.menuItemId, line.variantId, [...line.addOnIds].sort().join('+'), line.note.trim()].join('|')
    const existing = lines.value.find((l) => l.key === key)
    if (existing) existing.quantity = Math.min(99, existing.quantity + line.quantity)
    else lines.value.push({ ...line, key })
  }

  function setQty(key: string, qty: number) {
    const l = lines.value.find((x) => x.key === key)
    if (!l) return
    if (qty <= 0) lines.value = lines.value.filter((x) => x.key !== key)
    else l.quantity = Math.min(99, qty)
  }

  function clear() {
    lines.value = []
    note.value = ''
    idempotencyKey.value = null
  }

  /** Re-prices the cart from a fresh menu; drops lines whose item/variant/add-on is no longer sold. Returns true if anything changed. */
  function reprice(menu: PublicMenu): boolean {
    let changed = false
    const next: CartLine[] = []
    for (const l of lines.value) {
      const item = menu.items.find((i) => i.id === l.menuItemId)
      const variant = item?.variants.find((v) => v.id === l.variantId)
      const addOns = l.addOnIds.map((id) => item?.addOns.find((a) => a.id === id))
      if (!item || !variant || addOns.some((a) => !a)) {
        changed = true
        continue
      }
      const addOnTotal = addOns.reduce((a, x) => a + (x?.price ?? 0), 0)
      if (variant.price !== l.unitPrice || addOnTotal !== l.addOnTotal) changed = true
      next.push({ ...l, unitPrice: variant.price, addOnTotal })
    }
    lines.value = next
    return changed
  }

  function ensureIdempotencyKey() {
    idempotencyKey.value ??= crypto.randomUUID()
    return idempotencyKey.value
  }

  return { branchCode, tableCode, lines, note, count, total, init, add, setQty, clear, reprice, ensureIdempotencyKey, idempotencyKey }
})

const RECENT_KEY = 'brewline.recentOrders'
export interface RecentOrder { token: string; orderNumber: number; branchCode: string; createdAt: string }

export function rememberOrder(o: RecentOrder) {
  const list: RecentOrder[] = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]')
  localStorage.setItem(RECENT_KEY, JSON.stringify([o, ...list.filter((x) => x.token !== o.token)].slice(0, 5)))
}

export function recentOrders(branchCode: string): RecentOrder[] {
  try {
    const list: RecentOrder[] = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]')
    const dayAgo = Date.now() - 86400000
    return list.filter((o) => o.branchCode === branchCode.toUpperCase() && new Date(o.createdAt).getTime() > dayAgo)
  } catch {
    return []
  }
}
