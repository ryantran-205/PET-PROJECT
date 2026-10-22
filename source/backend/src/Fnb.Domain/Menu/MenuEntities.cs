using Fnb.Domain.Common;

namespace Fnb.Domain.Menu;

public sealed class MenuCategory : Entity
{
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MenuItem : Entity, IHasTimestamps, IHasRowVersion
{
    public Guid CategoryId { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public MenuCategory? Category { get; set; }
    public List<MenuItemVariant> Variants { get; set; } = [];
    public List<MenuItemAddOn> AddOns { get; set; } = [];
}

public sealed class MenuItemVariant : Entity
{
    public Guid MenuItemId { get; set; }
    /// <summary>e.g. "S", "M", "L" or "Mặc định".</summary>
    public required string Name { get; set; }
    public decimal BasePrice { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AddOn : Entity
{
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Which add-ons are allowed for a menu item.</summary>
public sealed class MenuItemAddOn
{
    public Guid MenuItemId { get; set; }
    public Guid AddOnId { get; set; }
    public AddOn? AddOn { get; set; }
}

/// <summary>Per-branch availability of a menu item. Missing row = not sold at that branch.</summary>
public sealed class BranchMenuItem : IBranchScoped
{
    public Guid BranchId { get; set; }
    public Guid MenuItemId { get; set; }
    public bool IsAvailable { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Optional per-branch price override for a variant.</summary>
public sealed class BranchVariantPrice : IBranchScoped
{
    public Guid BranchId { get; set; }
    public Guid VariantId { get; set; }
    public decimal Price { get; set; }
}

/// <summary>
/// Ingredient consumption per unit of a variant or an add-on.
/// Exactly one of <see cref="VariantId"/> or <see cref="AddOnId"/> is set.
/// </summary>
public sealed class RecipeLine : Entity
{
    public Guid? VariantId { get; set; }
    public Guid? AddOnId { get; set; }
    public Guid IngredientId { get; set; }
    /// <summary>Quantity in the ingredient's base unit, precision (18,3).</summary>
    public decimal Quantity { get; set; }
}
