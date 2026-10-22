using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Application.Inventory;
using Fnb.Contracts;
using Fnb.Domain.Common;
using Fnb.Domain.Identity;
using Fnb.Domain.Inventory;
using Fnb.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Orders;

/// <summary>
/// Staff-side order operations. All transitions go through <see cref="Order.TransitionTo"/> (state machine + actor rules),
/// are protected by rowversion optimistic concurrency, and emit outbox events in the same transaction.
/// Inventory is consumed on Confirmed and reversed (compensating ledger entries) on later cancellation.
/// </summary>
public sealed class OrderService(
    IAppDbContext db,
    IClock clock,
    IOutbox outbox,
    IAuditService audit,
    ICurrentUser currentUser,
    InventoryLedgerWriter ledger,
    InvoiceService invoices)
{
    public async Task<PagedResult<OrderSummaryDto>> ListAsync(Guid branchId, OrderQuery query, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        var q = db.Orders.AsNoTracking().Where(o => o.BranchId == branchId);
        if (query.ActiveOnly)
        {
            q = q.Where(o => OrderStatusGroups.Active.Contains(o.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var statuses = query.Status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<OrderStatus>(s, true, out var st) ? st : (OrderStatus?)null)
                .Where(s => s is not null).Select(s => s!.Value).ToList();
            q = q.Where(o => statuses.Contains(o.Status));
        }

        if (query.From is { } from)
        {
            q = q.Where(o => o.BusinessDate >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(o => o.BusinessDate <= to);
        }

        if (query.OrderNumber is { } n)
        {
            q = q.Where(o => o.OrderNumber == n);
        }

        var p = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var orders = await q.OrderByDescending(o => o.CreatedAtUtc).Skip(p.Skip).Take(p.SafePageSize)
            .Include(o => o.Items).ThenInclude(i => i.AddOns).AsSplitQuery().ToListAsync(ct);
        return new PagedResult<OrderSummaryDto>(orders.Select(ToSummary).ToList(), p.SafePage, p.SafePageSize, total);
    }

    public async Task<OrderDetailDto> GetAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.AddOns).Include(o => o.StatusHistory)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new NotFoundException("Order", orderId);
        currentUser.EnsureBranchAccess(order.BranchId);
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.RecordedAtUtc).ToListAsync(ct);
        var invoiceId = await db.Invoices.AsNoTracking().Where(i => i.OrderId == orderId).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        return ToDetail(order, payments, invoiceId);
    }

    public Task<OrderDetailDto> ConfirmAsync(Guid id, OrderActionRequest r, CancellationToken ct) => TransitionAsync(id, OrderStatus.Confirmed, r, ct);

    public Task<OrderDetailDto> StartPreparingAsync(Guid id, OrderActionRequest r, CancellationToken ct) => TransitionAsync(id, OrderStatus.Preparing, r, ct);

    public Task<OrderDetailDto> MarkReadyAsync(Guid id, OrderActionRequest r, CancellationToken ct) => TransitionAsync(id, OrderStatus.Ready, r, ct);

    public Task<OrderDetailDto> CompleteAsync(Guid id, OrderActionRequest r, CancellationToken ct) => TransitionAsync(id, OrderStatus.Completed, r, ct);

    public Task<OrderDetailDto> CancelAsync(Guid id, OrderActionRequest r, CancellationToken ct) => TransitionAsync(id, OrderStatus.Cancelled, r, ct);

    public Task<OrderDetailDto> RejectAsync(Guid id, OrderActionRequest r, CancellationToken ct) => TransitionAsync(id, OrderStatus.Rejected, r, ct);

    private async Task<OrderDetailDto> TransitionAsync(Guid orderId, OrderStatus to, OrderActionRequest request, CancellationToken ct)
    {
        new Validator().MaxLength(request.Reason, "reason", 300).ThrowIfInvalid();
        var userId = currentUser.RequireUserId();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.Include(o => o.Items).ThenInclude(i => i.AddOns).Include(o => o.StatusHistory).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new NotFoundException("Order", orderId);
        currentUser.EnsureBranchAccess(order.BranchId);
        Branches.ConcurrencyHelper.ApplyExpectedRowVersion(db, order, request.RowVersion);

        var actor = currentUser.IsBranchAdminOrAbove() ? OrderActor.BranchAdmin : OrderActor.Staff;
        var from = order.Status;
        var now = clock.UtcNow;
        order.TransitionTo(to, actor, userId, string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), now);

        var branch = await db.Branches.AsNoTracking().FirstAsync(b => b.Id == order.BranchId, ct);
        if (OrderStateMachine.ConsumesInventory(to) && !order.InventoryDeducted)
        {
            await ConsumeInventoryAsync(order, branch.AllowNegativeStock, userId, ct);
            order.InventoryDeducted = true;
            order.NeedsAttention = false;
            order.AttentionNote = null;
        }

        if (to is OrderStatus.Cancelled or OrderStatus.Rejected)
        {
            if (order.InventoryDeducted)
            {
                await ReverseInventoryAsync(order, userId, ct);
            }

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == order.Id, ct);
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                // No payment provider: refunds are a manual, recorded action by an admin.
                order.NeedsAttention = true;
                order.AttentionNote = "Đơn đã thanh toán bị hủy — cần hoàn tiền thủ công và ghi nhận hoàn tiền.";
            }
            else if (invoice is not null)
            {
                invoice.Status = InvoiceStatus.Voided;
            }
        }

        outbox.Enqueue(Topics.OrderEvents, MessageTypes.OrderStatusChanged,
            new OrderStatusChangedV1(order.Id, order.BranchId, order.OrderNumber, order.BusinessDate, from.ToString(), to.ToString(), now),
            currentUser.CorrelationId);
        if (to is OrderStatus.Cancelled or OrderStatus.Rejected || currentUser.IsSuperAdmin() && !currentUser.BranchIds.Contains(order.BranchId))
        {
            audit.Record($"order.{to.ToString().ToLowerInvariant()}", "Order", order.Id.ToString(), order.BranchId,
                new { from = from.ToString(), to = to.ToString(), request.Reason, order.OrderNumber });
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("concurrent_update", "Đơn vừa được người khác cập nhật, vui lòng tải lại.");
        }

        return await GetAsync(order.Id, ct);
    }

    private async Task ConsumeInventoryAsync(Order order, bool allowNegative, Guid userId, CancellationToken ct)
    {
        var variantIds = order.Items.Select(i => i.VariantId).Distinct().ToList();
        var addOnIds = order.Items.SelectMany(i => i.AddOns.Select(a => a.AddOnId)).Distinct().ToList();
        var recipeLines = await db.RecipeLines.AsNoTracking()
            .Where(l => (l.VariantId != null && variantIds.Contains(l.VariantId.Value)) || (l.AddOnId != null && addOnIds.Contains(l.AddOnId.Value)))
            .ToListAsync(ct);
        var variantRecipes = recipeLines.Where(l => l.VariantId != null).GroupBy(l => l.VariantId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RecipeComponent>)g.Select(l => new RecipeComponent(l.IngredientId, l.Quantity)).ToList());
        var addOnRecipes = recipeLines.Where(l => l.AddOnId != null).GroupBy(l => l.AddOnId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RecipeComponent>)g.Select(l => new RecipeComponent(l.IngredientId, l.Quantity)).ToList());

        var required = RecipeCalculator.Requirements(
            order.Items.Select(i => new ConsumptionLine(i.VariantId, i.AddOns.Select(a => a.AddOnId).ToList(), i.Quantity)),
            variantRecipes, addOnRecipes);
        var movements = required.Select(r => new StockMovement(r.Key, -r.Value, $"order:{order.Id}:consume:{r.Key}")).ToList();

        try
        {
            await ledger.ApplyAsync(order.BranchId, movements, LedgerEntryType.OrderConsumption, "Order", order.Id,
                $"Đơn #{order.OrderNumber}", userId, allowNegative, ct);
        }
        catch (BusinessRuleException ex) when (ex.Code == "insufficient_stock")
        {
            var ids = ex.Details?.Keys.Select(Guid.Parse).ToList() ?? [];
            var names = await db.Ingredients.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.Name).ToListAsync(ct);
            throw new BusinessRuleException("insufficient_stock",
                $"Không đủ nguyên liệu để xác nhận đơn: {string.Join(", ", names)}. Vui lòng nhập kho hoặc từ chối đơn.", ex.Details);
        }
    }

    private async Task ReverseInventoryAsync(Order order, Guid userId, CancellationToken ct)
    {
        var consumed = await db.InventoryLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Order" && e.SourceId == order.Id && e.EntryType == LedgerEntryType.OrderConsumption)
            .GroupBy(e => e.IngredientId)
            .Select(g => new { IngredientId = g.Key, Qty = g.Sum(e => e.QuantityChange) })
            .ToListAsync(ct);
        var movements = consumed.Select(c => new StockMovement(c.IngredientId, -c.Qty, $"order:{order.Id}:reverse:{c.IngredientId}")).ToList();
        await ledger.ApplyAsync(order.BranchId, movements, LedgerEntryType.OrderReversal, "Order", order.Id,
            $"Hoàn kho do hủy đơn #{order.OrderNumber}", userId, allowNegative: true, ct);
        order.InventoryDeducted = false;
    }

    // ---------- Payments ----------
    /// <summary>
    /// Records a staff-acknowledged payment (cash or bank-transfer QR). This is NOT an automatic bank verification.
    /// MVP rule: a single full payment per order (amount must equal the order total).
    /// </summary>
    public async Task<OrderDetailDto> RecordPaymentAsync(Guid orderId, RecordPaymentRequest request, CancellationToken ct)
    {
        var method = Enum.TryParse<PaymentMethod>(request.Method, true, out var m) && Enum.IsDefined(m)
            ? m : throw ValidationFailedException.For("method", "Phương thức thanh toán không hợp lệ.");
        new Validator()
            .Range(request.Amount, 0, 1_000_000_000, "amount")
            .MaxLength(request.Reference, "reference", 100)
            .MaxLength(request.Note, "note", 300)
            .ThrowIfInvalid();
        var userId = currentUser.RequireUserId();

        var branchId = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => (Guid?)o.BranchId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Order", orderId);
        currentUser.EnsureBranchAccess(branchId);

        // Invoice creation is idempotent and runs in its own transaction: the worker may be creating it concurrently
        // (on Confirmed), and a unique-index race must not abort the payment transaction.
        var invoice = await invoices.EnsureForOrderAsync(orderId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new NotFoundException("Order", orderId);
        currentUser.EnsureBranchAccess(order.BranchId);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
        {
            throw new BusinessRuleException("order_cancelled", "Không thể thanh toán đơn đã hủy.");
        }

        PaymentStateMachine.EnsureTransition(order.PaymentStatus, PaymentStatus.Paid);
        if (request.Amount != order.TotalAmount)
        {
            throw ValidationFailedException.For("amount", $"Số tiền phải bằng tổng đơn ({order.TotalAmount:0} VND).");
        }

        decimal? change = null;
        if (method == PaymentMethod.Cash && request.AmountTendered is { } tendered)
        {
            if (tendered < request.Amount)
            {
                throw ValidationFailedException.For("amountTendered", "Tiền khách đưa không đủ.");
            }

            change = tendered - request.Amount;
        }

        var now = clock.UtcNow;
        var payment = new Payment
        {
            BranchId = order.BranchId,
            OrderId = order.Id,
            InvoiceId = invoice.Id,
            Type = PaymentRecordType.Payment,
            Method = method,
            Amount = request.Amount,
            AmountTendered = method == PaymentMethod.Cash ? request.AmountTendered : null,
            ChangeAmount = change,
            Reference = request.Reference?.Trim(),
            Note = request.Note?.Trim(),
            RecordedByUserId = userId,
            RecordedAtUtc = now,
        };
        db.Payments.Add(payment);
        order.PaymentStatus = PaymentStatus.Paid;
        order.AttendantUserId ??= userId;
        order.UpdatedAtUtc = now;
        invoice.PaymentStatus = PaymentStatus.Paid;
        invoice.PaymentMethod = method;
        invoice.PaidAtUtc = now;
        invoice.AttendantUserId ??= userId;

        outbox.Enqueue(Topics.OrderEvents, MessageTypes.OrderPaymentChanged,
            new OrderPaymentChangedV1(order.Id, order.BranchId, order.OrderNumber, order.PaymentStatus.ToString()), currentUser.CorrelationId);
        audit.Record("payment.recorded", "Order", order.Id.ToString(), order.BranchId,
            new { payment.Id, method = method.ToString(), payment.Amount, invoice.InvoiceNo });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("concurrent_update", "Đơn vừa được cập nhật, vui lòng tải lại.");
        }

        return await GetAsync(order.Id, ct);
    }

    /// <summary>Records a manual refund (BranchAdmin+). No money is moved by the system.</summary>
    public async Task<OrderDetailDto> RefundAsync(Guid orderId, RefundRequest request, CancellationToken ct)
    {
        new Validator().Required(request.Reason, "reason", 300).ThrowIfInvalid();
        var userId = currentUser.RequireUserId();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new NotFoundException("Order", orderId);
        currentUser.EnsureBranchAdmin(order.BranchId);
        PaymentStateMachine.EnsureTransition(order.PaymentStatus, PaymentStatus.Refunded);
        var paid = await db.Payments.Where(p => p.OrderId == orderId && p.Type == PaymentRecordType.Payment).OrderByDescending(p => p.RecordedAtUtc).FirstAsync(ct);
        var now = clock.UtcNow;
        db.Payments.Add(new Payment
        {
            BranchId = order.BranchId,
            OrderId = order.Id,
            InvoiceId = paid.InvoiceId,
            Type = PaymentRecordType.Refund,
            Method = paid.Method,
            Amount = -paid.Amount,
            Note = request.Reason.Trim(),
            RecordedByUserId = userId,
            RecordedAtUtc = now,
        });
        order.PaymentStatus = PaymentStatus.Refunded;
        order.NeedsAttention = false;
        order.AttentionNote = null;
        order.UpdatedAtUtc = now;
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
        if (invoice is not null)
        {
            invoice.PaymentStatus = PaymentStatus.Refunded;
            if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
            {
                invoice.Status = InvoiceStatus.Voided;
            }
        }

        outbox.Enqueue(Topics.OrderEvents, MessageTypes.OrderPaymentChanged,
            new OrderPaymentChangedV1(order.Id, order.BranchId, order.OrderNumber, order.PaymentStatus.ToString()), currentUser.CorrelationId);
        audit.Record("payment.refunded", "Order", order.Id.ToString(), order.BranchId, new { amount = paid.Amount, request.Reason });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("concurrent_update", "Đơn vừa được cập nhật, vui lòng tải lại.");
        }

        return await GetAsync(order.Id, ct);
    }

    internal static OrderItemDto ToItemDto(OrderItem i) => new(i.Id, i.MenuItemId, i.VariantId, i.ItemName, i.VariantName, i.UnitPrice, i.AddOnUnitTotal,
        i.Quantity, i.LineTotal, i.Note, i.AddOns.Select(a => new OrderItemAddOnDto(a.AddOnId, a.Name, a.UnitPrice)).ToList());

    internal static OrderSummaryDto ToSummary(Order o) => new(o.Id, o.BranchId, o.OrderNumber, o.BusinessDate, o.Status.ToString(), o.PaymentStatus.ToString(),
        o.Channel.ToString(), o.TableName, o.TotalAmount, o.Items.Sum(i => i.Quantity), o.NeedsAttention, o.CreatedAtUtc, o.UpdatedAtUtc,
        o.Items.Select(ToItemDto).ToList(), o.CustomerNote, Convert.ToBase64String(o.RowVersion));

    private OrderDetailDto ToDetail(Order o, IReadOnlyList<Payment> payments, Guid? invoiceId)
    {
        var actor = currentUser.Role is UserRole.SuperAdmin or UserRole.BranchAdmin ? OrderActor.BranchAdmin : OrderActor.Staff;
        return new OrderDetailDto(o.Id, o.BranchId, o.OrderNumber, o.BusinessDate, o.Status.ToString(), o.PaymentStatus.ToString(), o.Channel.ToString(),
            o.TableName, o.CustomerNote, o.Subtotal, o.DiscountAmount, o.TaxAmount, o.RoundingAmount, o.TotalAmount, o.TaxRatePercent, o.PricesIncludeTax,
            o.PricingRuleSnapshot, o.InventoryDeducted, o.NeedsAttention, o.AttentionNote, o.CancelReason, o.AttendantUserId, o.CreatedAtUtc,
            o.ConfirmedAtUtc, o.CompletedAtUtc, o.CancelledAtUtc,
            o.Items.Select(ToItemDto).ToList(),
            o.StatusHistory.OrderBy(h => h.ChangedAtUtc).Select(h => new OrderHistoryDto(h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Actor.ToString(),
                h.ChangedByUserId, h.Reason, h.ChangedAtUtc)).ToList(),
            payments.Select(p => new PaymentDto(p.Id, p.Type.ToString(), p.Method.ToString(), p.Amount, p.AmountTendered, p.ChangeAmount, p.Reference, p.Note,
                p.RecordedByUserId, p.RecordedAtUtc)).ToList(),
            invoiceId,
            OrderStateMachine.NextStates(o.Status).Where(s => OrderStateMachine.IsActorAllowed(o.Status, s, actor) && s != OrderStatus.PendingConfirmation)
                .Select(s => s.ToString()).ToList(),
            Convert.ToBase64String(o.RowVersion));
    }
}
