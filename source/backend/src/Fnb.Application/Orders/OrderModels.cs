using Fnb.Domain.Orders;

namespace Fnb.Application.Orders;

public sealed record PublicOrderLineInput(Guid MenuItemId, Guid VariantId, int Quantity, IReadOnlyList<Guid>? AddOnIds, string? Note);

public sealed record CreatePublicOrderRequest(
    string BranchCode,
    string? TableCode,
    string? Channel,
    string? CustomerNote,
    IReadOnlyList<PublicOrderLineInput> Items,
    decimal? ExpectedTotal);

public sealed record PublicOrderItemDto(string Name, string Variant, IReadOnlyList<string> AddOns, int Quantity, decimal UnitPrice, decimal LineTotal, string? Note);

public sealed record PublicStatusStepDto(string Status, DateTime AtUtc);

public sealed record PublicOrderDto(
    Guid OrderId,
    int OrderNumber,
    DateOnly BusinessDate,
    string Status,
    string PaymentStatus,
    string BranchCode,
    string BranchName,
    string? TableName,
    IReadOnlyList<PublicOrderItemDto> Items,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    IReadOnlyList<PublicStatusStepDto> Timeline,
    bool CanCancel,
    string? TrackingToken);

public sealed record CancelPublicOrderRequest(string? Reason);

public sealed record PublicQueueEntryDto(int OrderNumber, string Status);

public sealed record PublicQueueDto(string BranchCode, string BranchName, DateOnly BusinessDate, IReadOnlyList<PublicQueueEntryDto> Orders);

public sealed record OrderItemAddOnDto(Guid AddOnId, string Name, decimal UnitPrice);

public sealed record OrderItemDto(Guid Id, Guid MenuItemId, Guid VariantId, string ItemName, string VariantName, decimal UnitPrice, decimal AddOnUnitTotal,
    int Quantity, decimal LineTotal, string? Note, IReadOnlyList<OrderItemAddOnDto> AddOns);

public sealed record OrderHistoryDto(string? FromStatus, string ToStatus, string Actor, Guid? ChangedByUserId, string? Reason, DateTime ChangedAtUtc);

public sealed record PaymentDto(Guid Id, string Type, string Method, decimal Amount, decimal? AmountTendered, decimal? ChangeAmount, string? Reference,
    string? Note, Guid RecordedByUserId, DateTime RecordedAtUtc);

public sealed record OrderSummaryDto(Guid Id, Guid BranchId, int OrderNumber, DateOnly BusinessDate, string Status, string PaymentStatus, string Channel,
    string? TableName, decimal TotalAmount, int ItemCount, bool NeedsAttention, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc,
    IReadOnlyList<OrderItemDto> Items, string? CustomerNote, string RowVersion);

public sealed record OrderDetailDto(
    Guid Id, Guid BranchId, int OrderNumber, DateOnly BusinessDate, string Status, string PaymentStatus, string Channel, string? TableName,
    string? CustomerNote, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal RoundingAmount, decimal TotalAmount,
    decimal TaxRatePercent, bool PricesIncludeTax, string PricingRuleSnapshot, bool InventoryDeducted, bool NeedsAttention, string? AttentionNote,
    string? CancelReason, Guid? AttendantUserId, DateTime CreatedAtUtc, DateTime? ConfirmedAtUtc, DateTime? CompletedAtUtc, DateTime? CancelledAtUtc,
    IReadOnlyList<OrderItemDto> Items, IReadOnlyList<OrderHistoryDto> History, IReadOnlyList<PaymentDto> Payments, Guid? InvoiceId,
    IReadOnlyList<string> AllowedNextStatuses, string RowVersion);

public sealed record OrderQuery(
    string? Status,
    DateOnly? From,
    DateOnly? To,
    int? OrderNumber,
    bool ActiveOnly = false,
    int Page = 1,
    int PageSize = 50);

public sealed record OrderActionRequest(string? Reason, string? RowVersion);

public sealed record RecordPaymentRequest(string Method, decimal Amount, decimal? AmountTendered, string? Reference, string? Note);

public sealed record RefundRequest(string Reason);

public static class OrderStatusGroups
{
    public static readonly OrderStatus[] Active =
        [OrderStatus.Submitted, OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready];
}
