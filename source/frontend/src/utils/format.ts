const vnd = new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 })
const num = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 })
const dt = new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' })
const time = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Ho_Chi_Minh' })

export const money = (v: number | null | undefined) => (v == null ? '—' : vnd.format(v))
export const qty = (v: number | null | undefined) => (v == null ? '—' : num.format(v))
export const dateTime = (iso: string | null | undefined) => (iso ? dt.format(new Date(iso)) : '—')
export const timeOnly = (iso: string | null | undefined) => (iso ? time.format(new Date(iso)) : '—')
export const minutesToHours = (m: number | null | undefined) => (m == null ? '—' : `${Math.floor(m / 60)}h${String(m % 60).padStart(2, '0')}`)

/** Business date (Asia/Ho_Chi_Minh) as yyyy-MM-dd. */
export function todayLocal(offsetDays = 0): string {
  const d = new Date(Date.now() + offsetDays * 86400000)
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(d)
}

export function toIsoDate(d: Date | string | null | undefined): string | undefined {
  if (!d) return undefined
  if (typeof d === 'string') return d.slice(0, 10)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export function elapsed(iso: string, now = Date.now()) {
  const mins = Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60000))
  return mins < 60 ? `${mins} phút` : `${Math.floor(mins / 60)}g ${mins % 60}p`
}

export const statusLabels: Record<string, string> = {
  Submitted: 'Mới gửi',
  PendingConfirmation: 'Chờ xác nhận',
  Confirmed: 'Đã xác nhận',
  Preparing: 'Đang pha chế',
  Ready: 'Sẵn sàng',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
  Rejected: 'Từ chối',
  Unpaid: 'Chưa thanh toán',
  Pending: 'Đang chờ',
  Paid: 'Đã thanh toán',
  Failed: 'Thất bại',
  Refunded: 'Đã hoàn tiền',
  Approved: 'Đã duyệt',
  Issued: 'Đã phát hành',
  Void: 'Đã hủy',
}

export const roleLabels: Record<string, string> = { SuperAdmin: 'Super Admin', BranchAdmin: 'Quản lý chi nhánh', BranchStaff: 'Nhân viên' }
export const methodLabels: Record<string, string> = { Cash: 'Tiền mặt', BankTransferQr: 'Chuyển khoản QR' }
