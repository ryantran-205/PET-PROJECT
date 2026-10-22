using Fnb.Domain.Common;

namespace Fnb.Domain.Organizations;

public sealed class Organization : Entity
{
    public required string Name { get; set; }
    public required string BrandName { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Branch : Entity, IHasTimestamps, IHasRowVersion
{
    public Guid OrganizationId { get; set; }
    /// <summary>Unique public code used in QR / kiosk URLs (e.g. "HCM-Q1").</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public string? Phone { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public bool IsActive { get; set; } = true;
    public bool PublicOrderingEnabled { get; set; } = true;

    // Invoice / receipt branding
    public string? InvoiceHeader { get; set; }
    public string? InvoiceFooter { get; set; }

    // Pricing configuration (no legal tax rate is assumed; default 0 until configured)
    public decimal TaxRatePercent { get; set; }
    public bool PricesIncludeTax { get; set; } = true;
    /// <summary>Total amount is rounded to a multiple of this unit (VND). 1 = no rounding beyond integer.</summary>
    public int RoundingUnit { get; set; } = 1;
    public bool AllowNegativeStock { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<BranchTable> Tables { get; set; } = [];
}

/// <summary>A table/kiosk point that has its own QR code. PublicCode is random and unguessable.</summary>
public sealed class BranchTable : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public required string Name { get; set; }
    public required string PublicCode { get; set; }
    public bool IsActive { get; set; } = true;
}
