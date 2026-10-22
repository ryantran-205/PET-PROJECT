using Fnb.Domain.Common;

namespace Fnb.Domain.Orders;

public enum InvoiceStatus
{
    Issued = 1,
    Voided = 2,
}

/// <summary>Internal invoice/receipt (NOT a legal e-invoice). All values are snapshots.</summary>
public sealed class Invoice : Entity, IBranchScoped, IHasRowVersion
{
    public Guid BranchId { get; set; }
    public Guid OrderId { get; set; }
    public required string InvoiceNo { get; set; }
    public int OrderNumber { get; set; }
    public DateOnly BusinessDate { get; set; }

    public required string BrandName { get; set; }
    public required string BranchName { get; set; }
    public required string BranchAddress { get; set; }
    public string? BranchPhone { get; set; }
    public string? Footer { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundingAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool PricesIncludeTax { get; set; }
    public required string PricingRuleSnapshot { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public PaymentMethod? PaymentMethod { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public Guid? AttendantUserId { get; set; }

    public DateTime IssuedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<InvoiceLine> Lines { get; set; } = [];
}

public sealed class InvoiceLine : Entity
{
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public required string ItemName { get; set; }
    public required string VariantName { get; set; }
    public string? AddOnsText { get; set; }
    public string? Note { get; set; }
    public int Quantity { get; set; }
    /// <summary>Unit price including add-ons.</summary>
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public enum PaymentRecordType
{
    Payment = 1,
    Refund = 2,
}

/// <summary>
/// A payment recorded by staff. Without a payment provider integration this reflects the staff acknowledgement,
/// not an automatic bank verification.
/// </summary>
public sealed class Payment : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? InvoiceId { get; set; }
    public PaymentRecordType Type { get; set; } = PaymentRecordType.Payment;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public decimal? AmountTendered { get; set; }
    public decimal? ChangeAmount { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public static class PaymentStateMachine
{
    private static readonly Dictionary<PaymentStatus, PaymentStatus[]> Allowed = new()
    {
        [PaymentStatus.Unpaid] = [PaymentStatus.Pending, PaymentStatus.Paid],
        [PaymentStatus.Pending] = [PaymentStatus.Paid, PaymentStatus.Failed],
        [PaymentStatus.Failed] = [PaymentStatus.Pending, PaymentStatus.Paid],
        [PaymentStatus.Paid] = [PaymentStatus.Refunded],
        [PaymentStatus.Refunded] = [],
    };

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) => Allowed[from].Contains(to);

    public static void EnsureTransition(PaymentStatus from, PaymentStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new BusinessRuleException("invalid_payment_transition", $"Payment status cannot move from {from} to {to}.");
        }
    }
}
