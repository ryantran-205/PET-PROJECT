using Fnb.Domain.Common;

namespace Fnb.Domain.Orders;

public enum OrderChannel
{
    TableQr = 1,
    Kiosk = 2,
    Counter = 3,
}

public enum PaymentStatus
{
    Unpaid = 1,
    Pending = 2,
    Paid = 3,
    Failed = 4,
    Refunded = 5,
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransferQr = 2,
}

public sealed class Order : Entity, IBranchScoped, IHasRowVersion
{
    public Guid BranchId { get; set; }
    public int OrderNumber { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid? TableId { get; set; }
    public string? TableName { get; set; }
    public OrderChannel Channel { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Submitted;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    /// <summary>SHA-256 hash of the public tracking token. The raw token is only returned once to the customer.</summary>
    public required string PublicTrackingTokenHash { get; set; }

    public string? CustomerNote { get; set; }

    // Pricing snapshot
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundingAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool PricesIncludeTax { get; set; }
    public required string PricingRuleSnapshot { get; set; }

    public bool InventoryDeducted { get; set; }
    public bool NeedsAttention { get; set; }
    public string? AttentionNote { get; set; }

    public Guid? AttendantUserId { get; set; }
    public string? CancelReason { get; set; }
    public string? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> StatusHistory { get; set; } = [];

    /// <summary>Applies a validated status transition and records history. Throws on invalid transition/actor.</summary>
    public OrderStatusHistory TransitionTo(OrderStatus to, OrderActor actor, Guid? userId, string? reason, DateTime nowUtc)
    {
        if (!OrderStateMachine.CanTransition(Status, to))
        {
            throw new BusinessRuleException(
                "invalid_order_transition",
                $"Order cannot move from {Status} to {to}.",
                new Dictionary<string, object?> { ["from"] = Status.ToString(), ["to"] = to.ToString() });
        }

        if (!OrderStateMachine.IsActorAllowed(Status, to, actor))
        {
            throw new ForbiddenException($"Actor {actor} is not allowed to move an order from {Status} to {to}.");
        }

        if (to is OrderStatus.Cancelled or OrderStatus.Rejected && string.IsNullOrWhiteSpace(reason))
        {
            throw ValidationFailedException.For("reason", "A reason is required to cancel or reject an order.");
        }

        var history = new OrderStatusHistory
        {
            OrderId = Id,
            FromStatus = Status,
            ToStatus = to,
            ChangedAtUtc = nowUtc,
            ChangedByUserId = userId,
            Actor = actor,
            Reason = reason,
        };

        Status = to;
        UpdatedAtUtc = nowUtc;
        switch (to)
        {
            case OrderStatus.Confirmed:
                ConfirmedAtUtc = nowUtc;
                AttendantUserId ??= userId;
                break;
            case OrderStatus.Completed:
                CompletedAtUtc = nowUtc;
                break;
            case OrderStatus.Cancelled or OrderStatus.Rejected:
                CancelledAtUtc = nowUtc;
                CancelReason = reason;
                break;
        }

        StatusHistory.Add(history);
        return history;
    }
}

public sealed class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid MenuItemId { get; set; }
    public Guid VariantId { get; set; }
    public required string ItemName { get; set; }
    public required string VariantName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal AddOnUnitTotal { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public List<OrderItemAddOn> AddOns { get; set; } = [];
}

public sealed class OrderItemAddOn : Entity
{
    public Guid OrderItemId { get; set; }
    public Guid AddOnId { get; set; }
    public required string Name { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class OrderStatusHistory : Entity
{
    public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public OrderActor Actor { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

/// <summary>Atomic counter row per (branch, business date). Updated via MERGE … WITH (HOLDLOCK).</summary>
public sealed class NumberSequence
{
    public required string Scope { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int LastNumber { get; set; }
}
