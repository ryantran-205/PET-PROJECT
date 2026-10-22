using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Inventory;
using Fnb.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fnb.Application.Reports;

public sealed record ReportRange(Guid? BranchId, DateOnly From, DateOnly To);

public sealed record RevenueByDayDto(DateOnly Date, decimal PaidRevenue, int PaidInvoices);

public sealed record RevenueByBranchDto(Guid BranchId, string BranchCode, string BranchName, decimal PaidRevenue, int PaidInvoices);

public sealed record RevenueByMethodDto(string Method, decimal Amount, int Count);

public sealed record RevenueReportDto(
    DateOnly From, DateOnly To, string Definition,
    decimal PaidRevenue, int PaidInvoices, decimal PaidTax, decimal PaidDiscount, decimal AverageOrderValue,
    decimal UnpaidAmount, int UnpaidInvoices, decimal RefundedAmount, int RefundedInvoices, int VoidedInvoices,
    IReadOnlyList<RevenueByDayDto> ByDay, IReadOnlyList<RevenueByBranchDto> ByBranch, IReadOnlyList<RevenueByMethodDto> ByMethod);

public sealed record TopItemDto(string ItemName, int Quantity, decimal Revenue);

public sealed record StatusCountDto(string Status, int Count);

public sealed record LowStockDto(Guid BranchId, string BranchCode, Guid IngredientId, string IngredientName, string Unit, decimal QuantityOnHand, decimal MinThreshold);

public sealed record DashboardDto(
    DateOnly From, DateOnly To, decimal PaidRevenue, int PaidInvoices, decimal AverageOrderValue, int TotalOrders,
    IReadOnlyList<StatusCountDto> OrdersByStatus, IReadOnlyList<TopItemDto> TopItems, IReadOnlyList<LowStockDto> LowStock,
    IReadOnlyList<RevenueByDayDto> RevenueByDay, decimal? WorkedHours, int? ShiftCount, IReadOnlyDictionary<string, string> Definitions);

/// <summary>
/// Reporting. Metric definitions (also documented in docs/reports-and-metrics.md):
/// - Paid revenue: Σ Invoice.TotalAmount where PaymentStatus = Paid and Status = Issued, by invoice business date.
/// - AOV: paid revenue / number of paid invoices.
/// - Orders by status: count of orders by business date (each order counted once).
/// - Top items: Σ quantity / line totals of order items for orders with status Completed, Ready, Preparing or Confirmed.
/// </summary>
public sealed class ReportService(IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IOptions<ReportOptions> options)
{
    public const string RevenueDefinition = "Doanh thu đã thanh toán = tổng TotalAmount của hóa đơn có PaymentStatus=Paid và Status=Issued, theo ngày kinh doanh của hóa đơn.";

    private static readonly OrderStatus[] SoldStatuses = [OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed];

    public Task<RevenueReportDto> RevenueAsync(ReportRange range, CancellationToken ct) => RevenueCoreAsync(Authorize(range), range, ct);

    private async Task<RevenueReportDto> RevenueCoreAsync(IReadOnlyCollection<Guid>? branches, ReportRange range, CancellationToken ct)
    {
        var invoices = ScopedInvoices(branches, range);

        var agg = await invoices.GroupBy(i => new { i.PaymentStatus, i.Status })
            .Select(g => new { g.Key.PaymentStatus, g.Key.Status, Total = g.Sum(i => i.TotalAmount), Tax = g.Sum(i => i.TaxAmount), Discount = g.Sum(i => i.DiscountAmount), Count = g.Count() })
            .ToListAsync(ct);
        var paid = agg.Where(a => a.PaymentStatus == PaymentStatus.Paid && a.Status == InvoiceStatus.Issued).ToList();
        var unpaid = agg.Where(a => a.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Pending or PaymentStatus.Failed && a.Status == InvoiceStatus.Issued).ToList();
        var refunded = agg.Where(a => a.PaymentStatus == PaymentStatus.Refunded).ToList();
        var paidRevenue = paid.Sum(a => a.Total);
        var paidCount = paid.Sum(a => a.Count);

        var paidQuery = invoices.Where(i => i.PaymentStatus == PaymentStatus.Paid && i.Status == InvoiceStatus.Issued);
        var byDay = (await paidQuery.GroupBy(i => i.BusinessDate)
                .Select(g => new { Date = g.Key, Total = g.Sum(i => i.TotalAmount), Count = g.Count() }).OrderBy(x => x.Date).ToListAsync(ct))
            .Select(x => new RevenueByDayDto(x.Date, x.Total, x.Count)).ToList();
        var byBranchRaw = await paidQuery.GroupBy(i => i.BranchId)
            .Select(g => new { BranchId = g.Key, Total = g.Sum(i => i.TotalAmount), Count = g.Count() }).ToListAsync(ct);
        var branchNames = await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => new { b.Code, b.Name }, ct);
        var byMethod = await paidQuery.GroupBy(i => i.PaymentMethod)
            .Select(g => new { Method = g.Key, Total = g.Sum(i => i.TotalAmount), Count = g.Count() }).ToListAsync(ct);

        return new RevenueReportDto(range.From, range.To, RevenueDefinition, paidRevenue, paidCount, paid.Sum(a => a.Tax), paid.Sum(a => a.Discount),
            paidCount == 0 ? 0 : Math.Round(paidRevenue / paidCount, 0, MidpointRounding.AwayFromZero),
            unpaid.Sum(a => a.Total), unpaid.Sum(a => a.Count), refunded.Sum(a => a.Total), refunded.Sum(a => a.Count),
            agg.Where(a => a.Status == InvoiceStatus.Voided).Sum(a => a.Count),
            byDay,
            byBranchRaw.Select(b => new RevenueByBranchDto(b.BranchId, branchNames.GetValueOrDefault(b.BranchId)?.Code ?? "", branchNames.GetValueOrDefault(b.BranchId)?.Name ?? "", b.Total, b.Count))
                .OrderByDescending(b => b.PaidRevenue).ToList(),
            byMethod.Select(m => new RevenueByMethodDto(m.Method?.ToString() ?? "Unknown", m.Total, m.Count)).ToList());
    }

    /// <summary>Streams the revenue CSV (UTF-8 BOM). Columns follow the specification.</summary>
    public async Task ExportRevenueCsvAsync(ReportRange range, Stream output, CancellationToken ct)
    {
        var branches = Authorize(range);
        var q = ScopedInvoices(branches, range);
        var count = await q.CountAsync(ct);
        if (count > options.Value.MaxExportRows)
        {
            throw new BusinessRuleException("export_too_large", $"Quá {options.Value.MaxExportRows} dòng; vui lòng thu hẹp khoảng thời gian.");
        }

        audit.Record("report.revenue_exported", "Report", "revenue.csv", range.BranchId, new { range.From, range.To, rows = count });
        await db.SaveChangesAsync(ct);

        await using var csv = new CsvWriter(output);
        await csv.WriteRowAsync("InvoiceNo", "BranchId", "CreatedDate", "AttendantStaffId", "OrderNumber", "TotalAmount", "VatAmount", "DiscountAmount", "PaymentMethod", "Status");
        await foreach (var i in q.OrderBy(i => i.IssuedAtUtc)
            .Select(i => new { i.InvoiceNo, i.BranchId, i.IssuedAtUtc, i.AttendantUserId, i.OrderNumber, i.TotalAmount, i.TaxAmount, i.DiscountAmount, i.PaymentMethod, i.PaymentStatus, i.Status })
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            var status = i.Status == InvoiceStatus.Voided ? "Voided" : i.PaymentStatus.ToString();
            await csv.WriteRowAsync(i.InvoiceNo, i.BranchId, DateTime.SpecifyKind(i.IssuedAtUtc, DateTimeKind.Utc), i.AttendantUserId, i.OrderNumber,
                i.TotalAmount, i.TaxAmount, i.DiscountAmount, i.PaymentMethod?.ToString() ?? "", status);
        }
    }

    /// <summary>
    /// Inventory movement summary per branch/ingredient (opening, receipts, issues, consumption, reversals, adjustments, closing) —
    /// independent from sales invoices so stock vs. sales can be reconciled.
    /// </summary>
    public async Task ExportInventoryCsvAsync(ReportRange range, Stream output, CancellationToken ct)
    {
        var branches = Authorize(range);
        var tzBranches = await db.Branches.AsNoTracking()
            .Where(b => branches == null || branches.Contains(b.Id))
            .Select(b => new { b.Id, b.Code, b.TimeZoneId }).ToListAsync(ct);
        var ingredients = await db.Ingredients.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);

        audit.Record("report.inventory_exported", "Report", "inventory.csv", range.BranchId, new { range.From, range.To });
        await db.SaveChangesAsync(ct);

        await using var csv = new CsvWriter(output);
        await csv.WriteRowAsync("BranchCode", "BranchId", "FromDate", "ToDate", "IngredientCode", "IngredientName", "Unit", "Opening", "Receipts", "Issues",
            "OrderConsumption", "OrderReversal", "Adjustments", "Stocktake", "Closing");
        foreach (var b in tzBranches.OrderBy(b => b.Code))
        {
            var (startUtc, endUtc) = BusinessTime.UtcRange(range.From, range.To, b.TimeZoneId);
            var opening = await db.InventoryLedgerEntries.AsNoTracking().Where(e => e.BranchId == b.Id && e.CreatedAtUtc < startUtc)
                .GroupBy(e => e.IngredientId).Select(g => new { g.Key, Qty = g.Sum(e => e.QuantityChange) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
            var movements = await db.InventoryLedgerEntries.AsNoTracking().Where(e => e.BranchId == b.Id && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc < endUtc)
                .GroupBy(e => new { e.IngredientId, e.EntryType }).Select(g => new { g.Key.IngredientId, g.Key.EntryType, Qty = g.Sum(e => e.QuantityChange) })
                .ToListAsync(ct);
            var ids = opening.Keys.Union(movements.Select(m => m.IngredientId)).Distinct();
            foreach (var id in ids.OrderBy(i => ingredients.GetValueOrDefault(i)?.Code))
            {
                decimal Sum(LedgerEntryType t) => movements.Where(m => m.IngredientId == id && m.EntryType == t).Sum(m => m.Qty);
                var open = opening.GetValueOrDefault(id);
                var close = open + movements.Where(m => m.IngredientId == id).Sum(m => m.Qty);
                var ing = ingredients.GetValueOrDefault(id);
                await csv.WriteRowAsync(b.Code, b.Id, range.From, range.To, ing?.Code, ing?.Name, ing?.Unit, open,
                    Sum(LedgerEntryType.Receipt), Sum(LedgerEntryType.Issue), Sum(LedgerEntryType.OrderConsumption), Sum(LedgerEntryType.OrderReversal),
                    Sum(LedgerEntryType.Adjustment), Sum(LedgerEntryType.Stocktake), close);
            }
        }
    }

    public async Task<DashboardDto> DashboardAsync(ReportRange range, CancellationToken ct)
    {
        // Staff may open the dashboard, but only for branches they are assigned to (ResolveBranchFilter enforces scope).
        var branches = Authorize(range, requireAdmin: false);
        var revenue = await RevenueCoreAsync(branches, range, ct);

        var orders = db.Orders.AsNoTracking().Where(o => o.BusinessDate >= range.From && o.BusinessDate <= range.To);
        if (branches is not null)
        {
            orders = orders.Where(o => branches.Contains(o.BranchId));
        }

        var byStatus = await orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var topItems = (await db.OrderItems.AsNoTracking()
                .Where(i => orders.Where(o => SoldStatuses.Contains(o.Status)).Select(o => o.Id).Contains(i.OrderId))
                .GroupBy(i => i.ItemName)
                .Select(g => new { Name = g.Key, Quantity = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.LineTotal) })
                .OrderByDescending(x => x.Quantity).Take(10).ToListAsync(ct))
            .Select(x => new TopItemDto(x.Name, x.Quantity, x.Revenue)).ToList();

        var stock = db.BranchInventories.AsNoTracking().Where(b => b.QuantityOnHand < b.MinThreshold);
        if (branches is not null)
        {
            stock = stock.Where(b => branches.Contains(b.BranchId));
        }

        var lowStock = await stock.Join(db.Branches, s => s.BranchId, b => b.Id, (s, b) => new { s, b.Code })
            .OrderBy(x => x.Code).ThenBy(x => x.s.Ingredient!.Name).Take(50)
            .Select(x => new LowStockDto(x.s.BranchId, x.Code, x.s.IngredientId, x.s.Ingredient!.Name, x.s.Ingredient.Unit, x.s.QuantityOnHand, x.s.MinThreshold))
            .ToListAsync(ct);

        decimal? hours = null;
        int? shifts = null;
        if (currentUser.IsBranchAdminOrAbove())
        {
            var att = db.AttendanceRecords.AsNoTracking().Where(r => r.WorkDate >= range.From && r.WorkDate <= range.To && r.CheckOutUtc != null);
            if (branches is not null)
            {
                att = att.Where(r => branches.Contains(r.BranchId));
            }

            var minutes = await att.SumAsync(r => (int?)r.PayableMinutes, ct) ?? 0;
            hours = Math.Round(minutes / 60m, 2);
            shifts = await att.CountAsync(ct);
        }

        return new DashboardDto(range.From, range.To, revenue.PaidRevenue, revenue.PaidInvoices, revenue.AverageOrderValue, byStatus.Sum(s => s.Count),
            byStatus.Select(s => new StatusCountDto(s.Key.ToString(), s.Count)).OrderBy(s => s.Status).ToList(), topItems, lowStock, revenue.ByDay, hours, shifts,
            new Dictionary<string, string>
            {
                ["paidRevenue"] = RevenueDefinition,
                ["averageOrderValue"] = "Doanh thu đã thanh toán / số hóa đơn đã thanh toán.",
                ["ordersByStatus"] = "Số đơn theo trạng thái hiện tại, lọc theo ngày kinh doanh của đơn (mỗi đơn tính một lần).",
                ["topItems"] = "Tổng số lượng món của các đơn Confirmed/Preparing/Ready/Completed.",
                ["workedHours"] = "Tổng giờ công tính lương (PayableMinutes) của bản ghi chấm công đã check-out.",
            });
    }

    private IReadOnlyCollection<Guid>? Authorize(ReportRange range, bool requireAdmin = true)
    {
        if (requireAdmin && !currentUser.IsBranchAdminOrAbove())
        {
            throw new ForbiddenException("Chỉ quản trị viên được xem báo cáo.");
        }

        new Validator()
            .Must(range.To >= range.From, "to", "Ngày kết thúc phải sau ngày bắt đầu.")
            .Must(range.To.DayNumber - range.From.DayNumber <= options.Value.MaxRangeDays, "to", $"Khoảng tối đa {options.Value.MaxRangeDays} ngày.")
            .ThrowIfInvalid();
        return currentUser.ResolveBranchFilter(range.BranchId);
    }

    private IQueryable<Invoice> ScopedInvoices(IReadOnlyCollection<Guid>? branches, ReportRange range)
    {
        var q = db.Invoices.AsNoTracking().Where(i => i.BusinessDate >= range.From && i.BusinessDate <= range.To);
        return branches is null ? q : q.Where(i => branches.Contains(i.BranchId));
    }
}
