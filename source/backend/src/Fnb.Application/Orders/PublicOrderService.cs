using System.Text.Json;
using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Contracts;
using Fnb.Domain.Common;
using Fnb.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fnb.Application.Orders;

public sealed record CreateOrderResult(PublicOrderDto Order, bool Replayed);

/// <summary>
/// Anonymous order submission and tracking.
/// Flow: validate → recompute prices server-side → (tx) allocate order number + insert order + idempotency record + outbox → commit.
/// </summary>
public sealed class PublicOrderService(
    IAppDbContext db,
    IClock clock,
    ISequenceAllocator sequences,
    IOutbox outbox,
    ITrackingTokenService trackingTokens,
    IUniqueConstraintDetector uniqueDetector,
    ICurrentUser currentUser,
    IOptions<OrderingOptions> options,
    ILogger<PublicOrderService> logger)
{
    private OrderingOptions Opt => options.Value;

    public async Task<CreateOrderResult> CreateAsync(CreatePublicOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 100)
        {
            throw ValidationFailedException.For("Idempotency-Key", "Header Idempotency-Key (8–100 ký tự) là bắt buộc.");
        }

        ValidateShape(request);
        var key = $"public-order:{idempotencyKey}";
        var requestHash = SecureTokens.Sha256(JsonSerializer.Serialize(request, MessagingJson.Options));

        var replay = await TryReplayAsync(key, requestHash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var branchCode = request.BranchCode.Trim().ToUpperInvariant();
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode && b.IsActive, ct)
            ?? throw new NotFoundException("Branch");
        if (!branch.PublicOrderingEnabled)
        {
            throw new BusinessRuleException("ordering_disabled", "Chi nhánh tạm ngưng nhận đơn trực tuyến.");
        }

        Domain.Organizations.BranchTable? table = null;
        if (!string.IsNullOrWhiteSpace(request.TableCode))
        {
            table = await db.BranchTables.AsNoTracking().FirstOrDefaultAsync(t => t.PublicCode == request.TableCode && t.BranchId == branch.Id && t.IsActive, ct)
                ?? throw ValidationFailedException.For("tableCode", "Mã bàn không hợp lệ.");
        }

        var lines = await PriceLinesAsync(branch.Id, request.Items, ct);
        var policy = new PricingPolicy(branch.TaxRatePercent, branch.PricesIncludeTax, branch.RoundingUnit);
        var pricing = PricingCalculator.Calculate(lines.Select(l => new PricingLine(l.UnitPrice, l.AddOnUnitTotal, l.Quantity)), 0m, policy);

        if (request.ExpectedTotal is { } expected && expected != pricing.TotalAmount)
        {
            throw new BusinessRuleException("price_changed", "Giá đã thay đổi, vui lòng kiểm tra lại giỏ hàng.",
                new Dictionary<string, object?> { ["expectedTotal"] = expected, ["currentTotal"] = pricing.TotalAmount });
        }

        var now = clock.UtcNow;
        var businessDate = BusinessTime.BusinessDate(now, branch.TimeZoneId);
        var order = new Order
        {
            BranchId = branch.Id,
            BusinessDate = businessDate,
            TableId = table?.Id,
            TableName = table?.Name,
            Channel = table is not null ? OrderChannel.TableQr : ParseChannel(request.Channel),
            CustomerNote = string.IsNullOrWhiteSpace(request.CustomerNote) ? null : request.CustomerNote.Trim(),
            Subtotal = pricing.Subtotal,
            DiscountAmount = pricing.DiscountAmount,
            TaxAmount = pricing.TaxAmount,
            RoundingAmount = pricing.RoundingAmount,
            TotalAmount = pricing.TotalAmount,
            TaxRatePercent = branch.TaxRatePercent,
            PricesIncludeTax = branch.PricesIncludeTax,
            PricingRuleSnapshot = pricing.RuleSnapshot,
            PublicTrackingTokenHash = "",
            CorrelationId = currentUser.CorrelationId,
            CreatedAtUtc = now,
        };
        order.PublicTrackingTokenHash = trackingTokens.Hash(trackingTokens.Create(order.Id));
        order.Items = lines.Select(l => new OrderItem
        {
            OrderId = order.Id,
            MenuItemId = l.MenuItemId,
            VariantId = l.VariantId,
            ItemName = l.ItemName,
            VariantName = l.VariantName,
            UnitPrice = l.UnitPrice,
            AddOnUnitTotal = l.AddOnUnitTotal,
            Quantity = l.Quantity,
            LineTotal = (l.UnitPrice + l.AddOnUnitTotal) * l.Quantity,
            Note = l.Note,
            AddOns = l.AddOns.Select(a => new OrderItemAddOn { AddOnId = a.Id, Name = a.Name, UnitPrice = a.Price }).ToList(),
        }).ToList();
        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.Submitted,
            Actor = OrderActor.Customer,
            ChangedAtUtc = now,
        });

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            order.OrderNumber = await sequences.NextAsync("order", branch.Id, businessDate, ct);
            db.Orders.Add(order);
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = key,
                RequestHash = requestHash,
                ResourceId = order.Id,
                StatusCode = 201,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(Opt.IdempotencyHours),
            });
            outbox.Enqueue(Topics.OrderEvents, MessageTypes.OrderSubmitted,
                new OrderSubmittedV1(order.Id, order.BranchId, order.OrderNumber, order.BusinessDate), currentUser.CorrelationId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (uniqueDetector.IsUniqueViolation(ex))
        {
            // Most likely a concurrent request with the same Idempotency-Key won the race.
            if (db is DbContext ctx)
            {
                ctx.ChangeTracker.Clear();
            }

            var raced = await TryReplayAsync(key, requestHash, ct);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }

        logger.LogInformation("Order {OrderId} #{OrderNumber} submitted at branch {BranchId}", order.Id, order.OrderNumber, order.BranchId);
        return new CreateOrderResult(ToPublicDto(order, branch.Code, branch.Name, trackingTokens.Create(order.Id)), false);
    }

    /// <summary>Queue display snapshot: today's active orders as number + status only (no customer data).</summary>
    public async Task<PublicQueueDto> GetQueueAsync(string branchCode, CancellationToken ct)
    {
        var code = (branchCode ?? "").Trim().ToUpperInvariant();
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code && b.IsActive, ct)
            ?? throw new NotFoundException("Branch");
        var businessDate = BusinessTime.BusinessDate(clock.UtcNow, branch.TimeZoneId);
        var statuses = new[] { OrderStatus.Submitted, OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready };
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.BranchId == branch.Id && o.BusinessDate == businessDate && statuses.Contains(o.Status))
            .OrderBy(o => o.OrderNumber)
            .Select(o => new { o.OrderNumber, o.Status })
            .Take(200)
            .ToListAsync(ct);
        return new PublicQueueDto(branch.Code, branch.Name, businessDate,
            rows.Select(r => new PublicQueueEntryDto(r.OrderNumber, r.Status.ToString())).ToList());
    }

    public async Task<PublicOrderDto> GetByTokenAsync(string token, CancellationToken ct)
    {
        var order = await LoadByTokenAsync(token, tracking: false, ct);
        var branch = await db.Branches.AsNoTracking().Where(b => b.Id == order.BranchId).Select(b => new { b.Code, b.Name }).FirstAsync(ct);
        return ToPublicDto(order, branch.Code, branch.Name, null);
    }

    public async Task<PublicOrderDto> CancelByTokenAsync(string token, CancelPublicOrderRequest request, CancellationToken ct)
    {
        if (!Opt.AllowCustomerCancel)
        {
            throw new BusinessRuleException("customer_cancel_disabled", "Vui lòng liên hệ quầy để hủy đơn.");
        }

        new Validator().MaxLength(request.Reason, "reason", 200).ThrowIfInvalid();
        var order = await LoadByTokenAsync(token, tracking: true, ct);
        if (!OrderStateMachine.IsActorAllowed(order.Status, OrderStatus.Cancelled, OrderActor.Customer))
        {
            throw new BusinessRuleException("cannot_cancel", "Đơn đã được xác nhận, vui lòng liên hệ quầy để hủy.");
        }

        var from = order.Status;
        order.TransitionTo(OrderStatus.Cancelled, OrderActor.Customer, null,
            string.IsNullOrWhiteSpace(request.Reason) ? "Khách hủy đơn" : request.Reason.Trim(), clock.UtcNow);
        outbox.Enqueue(Topics.OrderEvents, MessageTypes.OrderStatusChanged,
            new OrderStatusChangedV1(order.Id, order.BranchId, order.OrderNumber, order.BusinessDate, from.ToString(), order.Status.ToString(), clock.UtcNow),
            currentUser.CorrelationId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("concurrent_update", "Đơn vừa được cập nhật, vui lòng tải lại.");
        }

        var branch = await db.Branches.AsNoTracking().Where(b => b.Id == order.BranchId).Select(b => new { b.Code, b.Name }).FirstAsync(ct);
        return ToPublicDto(order, branch.Code, branch.Name, null);
    }

    private async Task<Order> LoadByTokenAsync(string token, bool tracking, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 100)
        {
            throw new NotFoundException("Order");
        }

        var hash = trackingTokens.Hash(token);
        var q = db.Orders.Include(o => o.Items).ThenInclude(i => i.AddOns).Include(o => o.StatusHistory).AsSplitQuery();
        var order = await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(o => o.PublicTrackingTokenHash == hash, ct);
        return order ?? throw new NotFoundException("Order");
    }

    private async Task<CreateOrderResult?> TryReplayAsync(string key, string requestHash, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (record is null || record.ExpiresAtUtc < clock.UtcNow)
        {
            if (record is not null)
            {
                await db.IdempotencyRecords.Where(r => r.Key == key).ExecuteDeleteAsync(ct);
            }

            return null;
        }

        if (record.RequestHash != requestHash)
        {
            throw new ConflictException("idempotency_key_reused", "Idempotency-Key đã được dùng cho một yêu cầu khác.");
        }

        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.AddOns).Include(o => o.StatusHistory).AsSplitQuery()
            .FirstAsync(o => o.Id == record.ResourceId, ct);
        var branch = await db.Branches.AsNoTracking().Where(b => b.Id == order.BranchId).Select(b => new { b.Code, b.Name }).FirstAsync(ct);
        return new CreateOrderResult(ToPublicDto(order, branch.Code, branch.Name, trackingTokens.Create(order.Id)), true);
    }

    private sealed record PricedAddOn(Guid Id, string Name, decimal Price);

    private sealed record PricedLine(Guid MenuItemId, Guid VariantId, string ItemName, string VariantName, decimal UnitPrice, decimal AddOnUnitTotal,
        int Quantity, string? Note, IReadOnlyList<PricedAddOn> AddOns);

    /// <summary>Validates availability and recomputes all prices from the database (client prices are never trusted).</summary>
    private async Task<List<PricedLine>> PriceLinesAsync(Guid branchId, IReadOnlyList<PublicOrderLineInput> inputs, CancellationToken ct)
    {
        var itemIds = inputs.Select(i => i.MenuItemId).Distinct().ToList();
        var items = await db.MenuItems.AsNoTracking().Include(i => i.Variants).Include(i => i.AddOns).ThenInclude(a => a.AddOn).Include(i => i.Category)
            .Where(i => itemIds.Contains(i.Id)).AsSplitQuery().ToDictionaryAsync(i => i.Id, ct);
        var available = (await db.BranchMenuItems.AsNoTracking().Where(b => b.BranchId == branchId && b.IsAvailable && itemIds.Contains(b.MenuItemId))
            .Select(b => b.MenuItemId).ToListAsync(ct)).ToHashSet();
        var variantIds = inputs.Select(i => i.VariantId).Distinct().ToList();
        var branchPrices = await db.BranchVariantPrices.AsNoTracking().Where(p => p.BranchId == branchId && variantIds.Contains(p.VariantId))
            .ToDictionaryAsync(p => p.VariantId, p => p.Price, ct);

        var unavailable = new List<object>();
        var result = new List<PricedLine>();
        foreach (var input in inputs)
        {
            if (!items.TryGetValue(input.MenuItemId, out var item) || !item.IsActive || item.Category?.IsActive != true || !available.Contains(item.Id))
            {
                unavailable.Add(new { input.MenuItemId, reason = "item_unavailable" });
                continue;
            }

            var variant = item.Variants.FirstOrDefault(v => v.Id == input.VariantId && v.IsActive);
            if (variant is null)
            {
                unavailable.Add(new { input.MenuItemId, input.VariantId, reason = "variant_unavailable", name = item.Name });
                continue;
            }

            var addOns = new List<PricedAddOn>();
            foreach (var addOnId in (input.AddOnIds ?? []).Distinct())
            {
                var link = item.AddOns.FirstOrDefault(a => a.AddOnId == addOnId && a.AddOn!.IsActive);
                if (link is null)
                {
                    unavailable.Add(new { input.MenuItemId, addOnId, reason = "addon_unavailable", name = item.Name });
                    continue;
                }

                addOns.Add(new PricedAddOn(link.AddOnId, link.AddOn!.Name, link.AddOn.Price));
            }

            var unitPrice = branchPrices.TryGetValue(variant.Id, out var bp) ? bp : variant.BasePrice;
            result.Add(new PricedLine(item.Id, variant.Id, item.Name, variant.Name, unitPrice, addOns.Sum(a => a.Price), input.Quantity,
                string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(), addOns));
        }

        if (unavailable.Count > 0)
        {
            throw new BusinessRuleException("items_unavailable", "Một số món/tùy chọn hiện không còn phục vụ.",
                new Dictionary<string, object?> { ["items"] = unavailable });
        }

        return result;
    }

    private void ValidateShape(CreatePublicOrderRequest r)
    {
        var v = new Validator()
            .Required(r.BranchCode, "branchCode", 30)
            .MaxLength(r.TableCode, "tableCode", 50)
            .MaxLength(r.CustomerNote, "customerNote", Opt.MaxNoteLength)
            .Must(r.Items is { Count: > 0 } && r.Items.Count <= Opt.MaxLinesPerOrder, "items", $"Đơn hàng cần 1–{Opt.MaxLinesPerOrder} dòng.");
        for (var i = 0; i < (r.Items?.Count ?? 0); i++)
        {
            var line = r.Items![i];
            v.NotEmpty(line.MenuItemId, $"items[{i}].menuItemId")
             .NotEmpty(line.VariantId, $"items[{i}].variantId")
             .Range(line.Quantity, 1, Opt.MaxQuantityPerLine, $"items[{i}].quantity")
             .MaxLength(line.Note, $"items[{i}].note", Opt.MaxNoteLength)
             .Must((line.AddOnIds?.Count ?? 0) <= Opt.MaxAddOnsPerLine, $"items[{i}].addOnIds", "Quá nhiều tùy chọn thêm.");
        }

        v.Must(r.ExpectedTotal is null or >= 0, "expectedTotal", "Giá trị không hợp lệ.");
        v.ThrowIfInvalid();
    }

    private static OrderChannel ParseChannel(string? c) =>
        Enum.TryParse<OrderChannel>(c, true, out var v) && Enum.IsDefined(v) && v != OrderChannel.TableQr ? v : OrderChannel.Kiosk;

    internal static PublicOrderDto ToPublicDto(Order o, string branchCode, string branchName, string? token) => new(
        o.Id, o.OrderNumber, o.BusinessDate, o.Status.ToString(), o.PaymentStatus.ToString(), branchCode, branchName, o.TableName,
        o.Items.Select(i => new PublicOrderItemDto(i.ItemName, i.VariantName, i.AddOns.Select(a => a.Name).ToList(), i.Quantity,
            i.UnitPrice + i.AddOnUnitTotal, i.LineTotal, i.Note)).ToList(),
        o.Subtotal, o.DiscountAmount, o.TaxAmount, o.TotalAmount, o.CreatedAtUtc,
        o.StatusHistory.OrderBy(h => h.ChangedAtUtc).Select(h => new PublicStatusStepDto(h.ToStatus.ToString(), h.ChangedAtUtc)).ToList(),
        OrderStateMachine.IsActorAllowed(o.Status, OrderStatus.Cancelled, OrderActor.Customer),
        token);
}
