import { ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, ApiError, errorMessage } from '@/api/http'
import type { Invoice, OrderDetail, OrderStatus } from '@/api/types'

export const actionMeta: Record<string, { endpoint: string; label: string; icon: string; severity?: string; needsReason?: boolean }> = {
  Confirmed: { endpoint: 'confirm', label: 'Xác nhận', icon: 'pi-check' },
  Preparing: { endpoint: 'start-preparing', label: 'Bắt đầu pha', icon: 'pi-play' },
  Ready: { endpoint: 'mark-ready', label: 'Sẵn sàng', icon: 'pi-bell', severity: 'success' },
  Completed: { endpoint: 'complete', label: 'Hoàn tất', icon: 'pi-check-circle', severity: 'success' },
  Rejected: { endpoint: 'reject', label: 'Từ chối', icon: 'pi-ban', severity: 'danger', needsReason: true },
  Cancelled: { endpoint: 'cancel', label: 'Hủy đơn', icon: 'pi-times', severity: 'danger', needsReason: true },
}

/** Shared order transitions for the orders list and the kitchen board. Concurrency conflicts surface as a reload prompt. */
export function useOrderActions(onChanged: () => void) {
  const toast = useToast()
  const busy = ref<string | null>(null)

  async function transition(order: { id: string; rowVersion?: string }, to: OrderStatus | string, reason?: string) {
    const meta = actionMeta[to]
    if (!meta) return false
    busy.value = order.id + to
    try {
      await api.post(`/orders/${order.id}/${meta.endpoint}`, { reason: reason ?? null, rowVersion: order.rowVersion ?? null })
      toast.add({ severity: 'success', summary: meta.label, detail: 'Đã cập nhật trạng thái đơn.', life: 2000 })
      onChanged()
      return true
    } catch (e) {
      const conflict = e instanceof ApiError && e.status === 409
      toast.add({
        severity: conflict ? 'warn' : 'error',
        summary: conflict ? 'Đơn vừa được cập nhật bởi người khác' : 'Không thực hiện được',
        detail: errorMessage(e),
        life: 5000,
      })
      if (conflict) onChanged()
      return false
    } finally {
      busy.value = null
    }
  }

  async function ensureInvoice(order: OrderDetail): Promise<Invoice | null> {
    try {
      return await api.post<Invoice>(`/orders/${order.id}/invoice`)
    } catch (e) {
      toast.add({ severity: 'error', summary: 'Không tạo được hóa đơn', detail: errorMessage(e), life: 5000 })
      return null
    }
  }

  async function downloadPdf(invoiceId: string, size: 'K80' | 'A5' = 'K80') {
    try {
      await api.download(`/invoices/${invoiceId}/pdf`, { size }, `invoice-${size}.pdf`)
    } catch (e) {
      toast.add({ severity: 'error', summary: 'Không tải được PDF', detail: errorMessage(e), life: 5000 })
    }
  }

  async function printPdf(invoiceId: string, size: 'K80' | 'A5' = 'K80') {
    try {
      const url = await api.blobUrl(`/invoices/${invoiceId}/pdf`, { size })
      const w = window.open(url, '_blank')
      if (!w) toast.add({ severity: 'warn', summary: 'Trình duyệt chặn cửa sổ bật lên', life: 3000 })
      setTimeout(() => URL.revokeObjectURL(url), 60000)
    } catch (e) {
      toast.add({ severity: 'error', summary: 'Không mở được PDF', detail: errorMessage(e), life: 5000 })
    }
  }

  return { busy, transition, ensureInvoice, downloadPdf, printPdf }
}

/** Short notification chime generated with WebAudio (no asset needed). Silently ignored if autoplay is blocked. */
export function chime() {
  try {
    const ctx = new AudioContext()
    const notes = [880, 1320]
    notes.forEach((f, i) => {
      const o = ctx.createOscillator()
      const g = ctx.createGain()
      o.frequency.value = f
      o.type = 'sine'
      g.gain.setValueAtTime(0.0001, ctx.currentTime + i * 0.16)
      g.gain.exponentialRampToValueAtTime(0.25, ctx.currentTime + i * 0.16 + 0.02)
      g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + i * 0.16 + 0.3)
      o.connect(g).connect(ctx.destination)
      o.start(ctx.currentTime + i * 0.16)
      o.stop(ctx.currentTime + i * 0.16 + 0.32)
    })
    setTimeout(() => ctx.close(), 1000)
  } catch {
    /* audio unavailable */
  }
}
