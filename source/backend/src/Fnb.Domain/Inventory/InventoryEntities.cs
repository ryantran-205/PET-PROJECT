using Fnb.Domain.Common;

namespace Fnb.Domain.Inventory;

public sealed class Ingredient : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Base unit of measure: g, ml, pcs ...</summary>
    public required string Unit { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Current balance per branch/ingredient. Must always reconcile with the ledger.</summary>
public sealed class BranchInventory : IBranchScoped, IHasRowVersion
{
    public Guid BranchId { get; set; }
    public Guid IngredientId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal MinThreshold { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Ingredient? Ingredient { get; set; }

    public bool IsLow => QuantityOnHand < MinThreshold;
}

public enum InventoryDocumentType
{
    Receipt = 1,
    Issue = 2,
    Adjustment = 3,
    Stocktake = 4,
}

public sealed class InventoryDocument : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public required string DocumentNo { get; set; }
    public InventoryDocumentType Type { get; set; }
    public required string Reason { get; set; }
    public string? Supplier { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<InventoryDocumentLine> Lines { get; set; } = [];
}

public sealed class InventoryDocumentLine : Entity
{
    public Guid DocumentId { get; set; }
    public Guid IngredientId { get; set; }
    /// <summary>Signed change applied to stock (receipt +, issue -, adjustment ±).</summary>
    public decimal QuantityChange { get; set; }
    /// <summary>For stocktake: the counted quantity.</summary>
    public decimal? CountedQuantity { get; set; }
    public decimal? UnitCost { get; set; }
}

public enum LedgerEntryType
{
    Receipt = 1,
    Issue = 2,
    Adjustment = 3,
    Stocktake = 4,
    OrderConsumption = 5,
    OrderReversal = 6,
}

/// <summary>Immutable inventory movement. Never updated or deleted by business code.</summary>
public sealed class InventoryLedgerEntry : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public Guid IngredientId { get; set; }
    public LedgerEntryType EntryType { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal BalanceAfter { get; set; }
    public required string SourceType { get; set; }
    public Guid SourceId { get; set; }
    /// <summary>Unique idempotency key, e.g. "order:{id}:consume:{ingredientId}".</summary>
    public required string ReferenceKey { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
