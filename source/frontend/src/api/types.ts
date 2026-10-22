export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export type Role = 'SuperAdmin' | 'BranchAdmin' | 'BranchStaff'

export interface BranchSummary { id: string; code: string; name: string }
export interface Me { id: string; userName: string; displayName: string; role: Role; branches: BranchSummary[]; employeeId?: string | null }

export interface BranchTable { id: string; name: string; publicCode: string; isActive: boolean }
export interface Branch {
  id: string; code: string; name: string; address: string; phone?: string | null; timeZoneId: string; isActive: boolean
  publicOrderingEnabled: boolean; invoiceHeader?: string | null; invoiceFooter?: string | null; taxRatePercent: number
  pricesIncludeTax: boolean; roundingUnit: number; allowNegativeStock: boolean; tables: BranchTable[]; rowVersion: string
}

export interface User { id: string; userName: string; displayName: string; email?: string | null; role: Role; isActive: boolean; branchIds: string[]; lastLoginAtUtc?: string | null; createdAtUtc: string }

export interface Employee {
  id: string; branchId: string; employeeCode: string; fullName: string; phone?: string | null; email?: string | null; position: string
  hourlyRate: number; isActive: boolean; userId?: string | null; rowVersion?: string
}
export interface WorkShift { id: string; branchId: string; name: string; startTime: string; endTime: string; payMultiplier: number; effectiveFrom: string; effectiveTo?: string | null; isActive: boolean; crossesMidnight: boolean }
export interface ShiftAssignment { id: string; branchId: string; employeeId: string; employeeName: string; workShiftId: string; shiftName: string; workDate: string; startTime: string; endTime: string; note?: string | null }
export interface Attendance {
  id: string; branchId: string; employeeId: string; employeeName: string; workDate: string; checkInUtc: string; checkOutUtc?: string | null
  workedMinutes?: number | null; payableMinutes?: number | null; roundingRule?: string | null; payMultiplier: number; isLocked: boolean; checkInSource: string; checkOutSource?: string | null
}
export interface LeaveRequest { id: string; branchId: string; employeeId: string; employeeName: string; startDate: string; endDate: string; reason: string; status: string; reviewNote?: string | null; reviewedAtUtc?: string | null; createdAtUtc: string }
export interface PayrollLine { employeeId: string; employeeCode: string; employeeName: string; shiftCount: number; workedMinutes: number; payableMinutes: number; weightedHours: number; hourlyRate: number; amount: number }
export interface PayrollSummary { branchId: string; from: string; to: string; formula: string; lines: PayrollLine[]; totalAmount: number; openRecords: number; closedPeriodId?: string | null }
export interface PayrollPeriod { id: string; branchId: string; periodStart: string; periodEnd: string; closedAtUtc: string; totalAmount: number; lineCount: number }

export interface Category { id: string; name: string; sortOrder: number; isActive: boolean }
export interface AddOn { id: string; name: string; price: number; isActive: boolean }
export interface Variant { id?: string; name: string; basePrice: number; sortOrder: number; isActive: boolean }
export interface MenuItem {
  id: string; categoryId: string; categoryName?: string; sku: string; name: string; description?: string | null; imageUrl?: string | null
  isActive: boolean; sortOrder: number; variants: Variant[]; addOnIds: string[]; rowVersion?: string
}
export interface RecipeLine { ingredientId: string; ingredientCode?: string; ingredientName?: string; unit?: string; quantity: number }
export interface BranchMenuItem { menuItemId: string; name: string; sku: string; categoryName: string; itemActive: boolean; isAvailable: boolean; variants: { variantId: string; name: string; basePrice: number; branchPrice?: number | null; effectivePrice: number }[] }

export interface Ingredient { id: string; code: string; name: string; unit: string; isActive: boolean }
export interface Stock { ingredientId: string; code: string; name: string; unit: string; quantityOnHand: number; minThreshold: number; isLow: boolean; updatedAtUtc: string }
export interface InventoryDocument { id: string; documentNo: string; type: string; reason: string; supplier?: string | null; createdAtUtc: string; lines: { ingredientId: string; quantityChange: number; countedQuantity?: number | null; unitCost?: number | null; balanceAfter: number }[] }
export interface LedgerEntry { id: string; ingredientId: string; ingredientCode: string; ingredientName: string; unit: string; entryType: string; quantityChange: number; balanceAfter: number; sourceType: string; sourceId: string; reason?: string | null; createdAtUtc: string }

export type OrderStatus = 'Submitted' | 'PendingConfirmation' | 'Confirmed' | 'Preparing' | 'Ready' | 'Completed' | 'Cancelled' | 'Rejected'
export interface OrderItemAddOn { addOnId: string; name: string; unitPrice: number }
export interface OrderItem { id: string; menuItemId: string; variantId: string; itemName: string; variantName: string; unitPrice: number; addOnUnitTotal: number; quantity: number; lineTotal: number; note?: string | null; addOns: OrderItemAddOn[] }
export interface OrderSummary {
  id: string; branchId: string; orderNumber: number; businessDate: string; status: OrderStatus; paymentStatus: string; channel: string; tableName?: string | null
  totalAmount: number; itemCount: number; needsAttention: boolean; createdAtUtc: string; updatedAtUtc?: string | null; items: OrderItem[]; customerNote?: string | null; rowVersion: string
}
export interface OrderHistory { fromStatus?: string | null; toStatus: string; actor: string; changedByUserId?: string | null; reason?: string | null; changedAtUtc: string }
export interface Payment { id: string; type: string; method: string; amount: number; amountTendered?: number | null; changeAmount?: number | null; reference?: string | null; note?: string | null; recordedAtUtc: string }
export interface OrderDetail extends Omit<OrderSummary, 'itemCount'> {
  customerNote?: string | null; subtotal: number; discountAmount: number; taxAmount: number; roundingAmount: number; taxRatePercent: number; pricesIncludeTax: boolean
  inventoryDeducted: boolean; attentionNote?: string | null; cancelReason?: string | null; confirmedAtUtc?: string | null; completedAtUtc?: string | null
  history: OrderHistory[]; payments: Payment[]; invoiceId?: string | null; allowedNextStatuses: OrderStatus[]
}

export interface Invoice {
  id: string; branchId: string; orderId: string; invoiceNo: string; orderNumber: number; businessDate: string; branchName: string; totalAmount: number
  taxAmount: number; discountAmount: number; subtotal: number; status: string; paymentStatus: string; paymentMethod?: string | null; paidAtUtc?: string | null; issuedAtUtc: string
  lines: { lineNo: number; itemName: string; variantName: string; addOnsText?: string | null; note?: string | null; quantity: number; unitPrice: number; lineTotal: number }[]
}

export interface RevenueReport {
  from: string; to: string; definition: string; paidRevenue: number; paidInvoices: number; paidTax: number; paidDiscount: number; averageOrderValue: number
  unpaidAmount: number; unpaidInvoices: number; refundedAmount: number; refundedInvoices: number; voidedInvoices: number
  byDay: { date: string; paidRevenue: number; paidInvoices: number }[]
  byBranch: { branchId: string; branchCode: string; branchName: string; paidRevenue: number; paidInvoices: number }[]
  byMethod: { method: string; amount: number; count: number }[]
}
export interface Dashboard {
  from: string; to: string; paidRevenue: number; paidInvoices: number; averageOrderValue: number; totalOrders: number
  ordersByStatus: { status: string; count: number }[]; topItems: { itemName: string; quantity: number; revenue: number }[]
  lowStock: { branchId: string; branchCode: string; ingredientId: string; ingredientName: string; unit: string; quantityOnHand: number; minThreshold: number }[]
  revenueByDay: { date: string; paidRevenue: number; paidInvoices: number }[]; workedHours?: number | null; shiftCount?: number | null; definitions: Record<string, string>
}

export interface PublicVariant { id: string; name: string; price: number }
export interface PublicAddOn { id: string; name: string; price: number }
export interface PublicMenuItem { id: string; categoryId: string; name: string; description?: string | null; imageUrl?: string | null; variants: PublicVariant[]; addOns: PublicAddOn[] }
export interface PublicMenu {
  branch: { code: string; name: string; address: string; orderingEnabled: boolean; currency: string; tableName?: string | null }
  categories: { id: string; name: string; sortOrder: number }[]; items: PublicMenuItem[]; generatedAtUtc: string
}
export interface PublicOrder {
  orderId: string; orderNumber: number; businessDate: string; status: OrderStatus; paymentStatus: string; branchCode: string; branchName: string; tableName?: string | null
  items: { name: string; variant: string; addOns: string[]; quantity: number; unitPrice: number; lineTotal: number; note?: string | null }[]
  subtotal: number; discountAmount: number; taxAmount: number; totalAmount: number; createdAtUtc: string
  timeline: { status: string; atUtc: string }[]; canCancel: boolean; trackingToken?: string | null
}
export interface AuditLog { id: string; actorUserId?: string | null; actorName: string; action: string; resourceType: string; resourceId?: string | null; branchId?: string | null; timestampUtc: string; correlationId?: string | null; ipAddress?: string | null; changes?: string | null }
