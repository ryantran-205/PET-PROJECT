using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Orders;

public sealed record InvoiceDto(Guid Id, Guid BranchId, Guid OrderId, string InvoiceNo, int OrderNumber, DateOnly BusinessDate, string BrandName,
    string BranchName, string BranchAddress, string? BranchPhone, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal RoundingAmount,
    decimal TotalAmount, decimal TaxRatePercent, bool PricesIncludeTax, string Status, string PaymentStatus, string? PaymentMethod, DateTime? PaidAtUtc,
    Guid? AttendantUserId, DateTime IssuedAtUtc, IReadOnlyList<InvoiceLineDto> Lines);

public sealed record InvoiceLineDto(int LineNo, string ItemName, string VariantName, string? AddOnsText, string? Note, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record InvoiceQuery(Guid? BranchId, DateOnly? From, DateOnly? To, string? PaymentStatus, int Page = 1, int PageSize = 50);

/// <summary>Creates internal invoices (receipts) idempotently: one invoice per order, enforced by a unique index on OrderId.</summary>
public sealed class InvoiceService(
    IAppDbContext db,
    IClock clock,
    ISequenceAllocator sequences,
    IUniqueConstraintDetector uniqueDetector,
    ICurrentUser currentUser,
    IInvoicePdfRenderer pdfRenderer)
{
    /// <summary>Returns the order's invoice, creating it with a full snapshot if missing. Safe under duplicate calls.</summary>
    public async Task<Invoice> EnsureForOrderAsync(Guid orderId, CancellationToken ct)
    {
        var existing = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.AddOns).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new NotFoundException("Order", orderId);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
        {
            throw new BusinessRuleException("order_cancelled", "Không thể lập hóa đơn cho đơn đã hủy/từ chối.");
        }

        var branch = await db.Branches.AsNoTracking().FirstAsync(b => b.Id == order.BranchId, ct);
        var org = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == branch.OrganizationId, ct);

        var ownsTx = db.Database.CurrentTransaction is null;
        var tx = ownsTx ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var seq = await sequences.NextAsync("invoice", branch.Id, order.BusinessDate, ct);
            var invoice = new Invoice
            {
                BranchId = branch.Id,
                OrderId = order.Id,
                InvoiceNo = $"HD-{branch.Code}-{order.BusinessDate:yyyyMMdd}-{seq:D5}",
                OrderNumber = order.OrderNumber,
                BusinessDate = order.BusinessDate,
                BrandName = branch.InvoiceHeader ?? org.BrandName,
                BranchName = branch.Name,
                BranchAddress = branch.Address,
                BranchPhone = branch.Phone,
                Footer = branch.InvoiceFooter,
                Subtotal = order.Subtotal,
                DiscountAmount = order.DiscountAmount,
                TaxAmount = order.TaxAmount,
                RoundingAmount = order.RoundingAmount,
                TotalAmount = order.TotalAmount,
                TaxRatePercent = order.TaxRatePercent,
                PricesIncludeTax = order.PricesIncludeTax,
                PricingRuleSnapshot = order.PricingRuleSnapshot,
                PaymentStatus = order.PaymentStatus,
                AttendantUserId = order.AttendantUserId,
                IssuedAtUtc = clock.UtcNow,
                Lines = order.Items.Select((i, idx) => new InvoiceLine
                {
                    LineNo = idx + 1,
                    ItemName = i.ItemName,
                    VariantName = i.VariantName,
                    AddOnsText = i.AddOns.Count == 0 ? null : string.Join(", ", i.AddOns.Select(a => a.Name)),
                    Note = i.Note,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice + i.AddOnUnitTotal,
                    LineTotal = i.LineTotal,
                }).ToList(),
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(ct);
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            return invoice;
        }
        catch (DbUpdateException ex) when (uniqueDetector.IsUniqueViolation(ex) && ownsTx)
        {
            // Created concurrently (e.g. worker + payment at the same time) — return the winner.
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            if (db is DbContext ctx)
            {
                foreach (var e in ctx.ChangeTracker.Entries<Invoice>().Where(e => e.State == EntityState.Added).ToList())
                {
                    e.State = EntityState.Detached;
                }

                foreach (var e in ctx.ChangeTracker.Entries<InvoiceLine>().Where(e => e.State == EntityState.Added).ToList())
                {
                    e.State = EntityState.Detached;
                }
            }

            return await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.OrderId == orderId, ct);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    public async Task<InvoiceDto> EnsureForOrderAsDtoAsync(Guid orderId, CancellationToken ct)
    {
        var branchId = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => (Guid?)o.BranchId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Order", orderId);
        currentUser.EnsureBranchAccess(branchId);
        return ToDto(await EnsureForOrderAsync(orderId, ct));
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("Invoice", id);
        currentUser.EnsureBranchAccess(invoice.BranchId);
        return ToDto(invoice);
    }

    public async Task<PagedResult<InvoiceDto>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        var branches = currentUser.ResolveBranchFilter(query.BranchId);
        var q = db.Invoices.AsNoTracking().Include(i => i.Lines).AsQueryable();
        if (branches is not null)
        {
            q = q.Where(i => branches.Contains(i.BranchId));
        }

        if (query.From is { } from)
        {
            q = q.Where(i => i.BusinessDate >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(i => i.BusinessDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.PaymentStatus) && Enum.TryParse<PaymentStatus>(query.PaymentStatus, true, out var ps))
        {
            q = q.Where(i => i.PaymentStatus == ps);
        }

        var p = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(i => i.IssuedAtUtc).Skip(p.Skip).Take(p.SafePageSize).AsSplitQuery().ToListAsync(ct);
        return new PagedResult<InvoiceDto>(items.Select(ToDto).ToList(), p.SafePage, p.SafePageSize, total);
    }

    public async Task<(byte[] Content, string FileName)> RenderPdfAsync(Guid id, InvoicePaperSize size, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("Invoice", id);
        currentUser.EnsureBranchAccess(invoice.BranchId);
        var branch = await db.Branches.AsNoTracking().FirstAsync(b => b.Id == invoice.BranchId, ct);
        var tableName = await db.Orders.AsNoTracking().Where(o => o.Id == invoice.OrderId).Select(o => o.TableName).FirstOrDefaultAsync(ct);

        var model = new InvoicePdfModel(
            invoice.BrandName, invoice.BranchName, invoice.BranchAddress, invoice.BranchPhone, invoice.InvoiceNo, invoice.OrderNumber,
            BusinessTime.ToLocal(invoice.IssuedAtUtc, branch.TimeZoneId), tableName,
            invoice.Lines.OrderBy(l => l.LineNo).Select(l => new InvoicePdfLine(l.ItemName, l.VariantName, l.AddOnsText, l.Note, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            invoice.Subtotal, invoice.DiscountAmount, invoice.TaxAmount, invoice.TaxRatePercent, invoice.PricesIncludeTax, invoice.RoundingAmount,
            invoice.TotalAmount, PaymentMethodLabel(invoice.PaymentMethod), PaymentStatusLabel(invoice.PaymentStatus, invoice.Status),
            invoice.Footer, null);
        // Tracking QR intentionally omitted: staff-side invoices don't hold the customer's tracking token (only its hash).
        var bytes = pdfRenderer.Render(model, size);
        return (bytes, $"{invoice.InvoiceNo}-{size}.pdf");
    }

    public static string PaymentMethodLabel(PaymentMethod? m) => m switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.BankTransferQr => "Chuyển khoản QR",
        _ => "Chưa thanh toán",
    };

    public static string PaymentStatusLabel(PaymentStatus s, InvoiceStatus status) => status == InvoiceStatus.Voided ? "Đã hủy" : s switch
    {
        PaymentStatus.Paid => "Đã thanh toán",
        PaymentStatus.Pending => "Chờ xác nhận",
        PaymentStatus.Failed => "Thanh toán lỗi",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        _ => "Chưa thanh toán",
    };

    internal static InvoiceDto ToDto(Invoice i) => new(i.Id, i.BranchId, i.OrderId, i.InvoiceNo, i.OrderNumber, i.BusinessDate, i.BrandName, i.BranchName,
        i.BranchAddress, i.BranchPhone, i.Subtotal, i.DiscountAmount, i.TaxAmount, i.RoundingAmount, i.TotalAmount, i.TaxRatePercent, i.PricesIncludeTax,
        i.Status.ToString(), i.PaymentStatus.ToString(), i.PaymentMethod?.ToString(), i.PaidAtUtc, i.AttendantUserId, i.IssuedAtUtc,
        i.Lines.OrderBy(l => l.LineNo).Select(l => new InvoiceLineDto(l.LineNo, l.ItemName, l.VariantName, l.AddOnsText, l.Note, l.Quantity, l.UnitPrice, l.LineTotal)).ToList());
}
