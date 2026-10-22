using Fnb.Application.Abstractions;
using Fnb.Application.Branches;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Menu;

public sealed record CategoryDto(Guid Id, string Name, int SortOrder, bool IsActive);

public sealed record UpsertCategoryRequest(string Name, int SortOrder, bool IsActive = true);

public sealed record AddOnDto(Guid Id, string Name, decimal Price, bool IsActive);

public sealed record UpsertAddOnRequest(string Name, decimal Price, bool IsActive = true);

public sealed record VariantDto(Guid Id, string Name, decimal BasePrice, int SortOrder, bool IsActive);

public sealed record MenuItemDto(Guid Id, Guid CategoryId, string CategoryName, string Sku, string Name, string? Description, string? ImageUrl,
    bool IsActive, int SortOrder, IReadOnlyList<VariantDto> Variants, IReadOnlyList<Guid> AddOnIds, string RowVersion);

public sealed record VariantInput(Guid? Id, string Name, decimal BasePrice, int SortOrder, bool IsActive = true);

public sealed record UpsertMenuItemRequest(Guid CategoryId, string Sku, string Name, string? Description, string? ImageUrl, bool IsActive, int SortOrder,
    IReadOnlyList<VariantInput> Variants, IReadOnlyList<Guid>? AddOnIds, string? RowVersion = null);

public sealed record MenuItemQuery(Guid? CategoryId, string? Search, bool? IsActive, int Page = 1, int PageSize = 50);

public sealed record RecipeLineDto(Guid IngredientId, string IngredientCode, string IngredientName, string Unit, decimal Quantity);

public sealed record RecipeLineInput(Guid IngredientId, decimal Quantity);

public sealed record UpdateRecipeRequest(IReadOnlyList<RecipeLineInput> Lines);

public sealed record BranchMenuVariantDto(Guid VariantId, string Name, decimal BasePrice, decimal? BranchPrice, decimal EffectivePrice);

public sealed record BranchMenuItemDto(Guid MenuItemId, string Name, string Sku, string CategoryName, bool ItemActive, bool IsAvailable,
    IReadOnlyList<BranchMenuVariantDto> Variants);

public sealed record BranchMenuVariantPriceInput(Guid VariantId, decimal? Price);

public sealed record BranchMenuItemInput(Guid MenuItemId, bool IsAvailable, IReadOnlyList<BranchMenuVariantPriceInput>? VariantPrices);

public sealed record UpdateBranchMenuRequest(IReadOnlyList<BranchMenuItemInput> Items);

/// <summary>
/// Chain menu catalogue (SuperAdmin) and per-branch availability/price overrides (BranchAdmin of that branch).
/// Every change invalidates the public menu cache.
/// </summary>
public sealed class MenuService(IAppDbContext db, IClock clock, IAuditService audit, ICurrentUser currentUser, ICacheService cache)
{
    // ---------- Categories ----------
    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        await db.MenuCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive)).ToListAsync(ct);

    public async Task<CategoryDto> CreateCategoryAsync(UpsertCategoryRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        new Validator().Required(r.Name, "name", 100).Range(r.SortOrder, 0, 10_000, "sortOrder").ThrowIfInvalid();
        var c = new MenuCategory { Name = r.Name.Trim(), SortOrder = r.SortOrder, IsActive = r.IsActive };
        db.MenuCategories.Add(c);
        audit.Record("menu.category_created", "MenuCategory", c.Id.ToString(), null, r);
        await db.SaveChangesAsync(ct);
        await InvalidatePublicMenusAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpsertCategoryRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        new Validator().Required(r.Name, "name", 100).Range(r.SortOrder, 0, 10_000, "sortOrder").ThrowIfInvalid();
        var c = await db.MenuCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("MenuCategory", id);
        c.Name = r.Name.Trim();
        c.SortOrder = r.SortOrder;
        c.IsActive = r.IsActive;
        audit.Record("menu.category_updated", "MenuCategory", c.Id.ToString(), null, r);
        await db.SaveChangesAsync(ct);
        await InvalidatePublicMenusAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive);
    }

    // ---------- Add-ons ----------
    public async Task<IReadOnlyList<AddOnDto>> ListAddOnsAsync(CancellationToken ct) =>
        await db.AddOns.AsNoTracking().OrderBy(a => a.Name).Select(a => new AddOnDto(a.Id, a.Name, a.Price, a.IsActive)).ToListAsync(ct);

    public async Task<AddOnDto> CreateAddOnAsync(UpsertAddOnRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        new Validator().Required(r.Name, "name", 100).Range(r.Price, 0, 10_000_000, "price").ThrowIfInvalid();
        var a = new AddOn { Name = r.Name.Trim(), Price = r.Price, IsActive = r.IsActive };
        db.AddOns.Add(a);
        audit.Record("menu.addon_created", "AddOn", a.Id.ToString(), null, r);
        await db.SaveChangesAsync(ct);
        await InvalidatePublicMenusAsync(ct);
        return new AddOnDto(a.Id, a.Name, a.Price, a.IsActive);
    }

    public async Task<AddOnDto> UpdateAddOnAsync(Guid id, UpsertAddOnRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        new Validator().Required(r.Name, "name", 100).Range(r.Price, 0, 10_000_000, "price").ThrowIfInvalid();
        var a = await db.AddOns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("AddOn", id);
        var before = new { a.Name, a.Price, a.IsActive };
        a.Name = r.Name.Trim();
        a.Price = r.Price;
        a.IsActive = r.IsActive;
        audit.Record("menu.addon_updated", "AddOn", a.Id.ToString(), null, new { before, after = r });
        await db.SaveChangesAsync(ct);
        await InvalidatePublicMenusAsync(ct);
        return new AddOnDto(a.Id, a.Name, a.Price, a.IsActive);
    }

    // ---------- Items ----------
    public async Task<PagedResult<MenuItemDto>> ListItemsAsync(MenuItemQuery query, CancellationToken ct)
    {
        var q = db.MenuItems.AsNoTracking().Include(i => i.Category).Include(i => i.Variants).Include(i => i.AddOns).AsQueryable();
        if (query.CategoryId is { } cid)
        {
            q = q.Where(i => i.CategoryId == cid);
        }

        if (query.IsActive is { } active)
        {
            q = q.Where(i => i.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(i => i.Name.Contains(s) || i.Sku.Contains(s));
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(i => i.Category!.SortOrder).ThenBy(i => i.SortOrder).ThenBy(i => i.Name)
            .Skip(page.Skip).Take(page.SafePageSize).AsSplitQuery().ToListAsync(ct);
        return new PagedResult<MenuItemDto>(items.Select(ToDto).ToList(), page.SafePage, page.SafePageSize, total);
    }

    public async Task<MenuItemDto> GetItemAsync(Guid id, CancellationToken ct)
    {
        var item = await db.MenuItems.AsNoTracking().Include(i => i.Category).Include(i => i.Variants).Include(i => i.AddOns)
            .AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("MenuItem", id);
        return ToDto(item);
    }

    public async Task<MenuItemDto> CreateItemAsync(UpsertMenuItemRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        await ValidateItemAsync(r, null, ct);
        var item = new MenuItem
        {
            CategoryId = r.CategoryId,
            Sku = r.Sku.Trim().ToUpperInvariant(),
            Name = r.Name.Trim(),
            Description = r.Description?.Trim(),
            ImageUrl = r.ImageUrl?.Trim(),
            IsActive = r.IsActive,
            SortOrder = r.SortOrder,
            CreatedAtUtc = clock.UtcNow,
            Variants = r.Variants.Select(v => new MenuItemVariant { Name = v.Name.Trim(), BasePrice = v.BasePrice, SortOrder = v.SortOrder, IsActive = v.IsActive }).ToList(),
        };
        item.AddOns = (r.AddOnIds ?? []).Distinct().Select(a => new MenuItemAddOn { MenuItemId = item.Id, AddOnId = a }).ToList();
        db.MenuItems.Add(item);
        audit.Record("menu.item_created", "MenuItem", item.Id.ToString(), null, new { item.Sku, item.Name, variants = r.Variants });
        await db.SaveChangesAsync(ct);
        await InvalidatePublicMenusAsync(ct);
        return await GetItemAsync(item.Id, ct);
    }

    public async Task<MenuItemDto> UpdateItemAsync(Guid id, UpsertMenuItemRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        var item = await db.MenuItems.Include(i => i.Variants).Include(i => i.AddOns).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("MenuItem", id);
        await ValidateItemAsync(r, id, ct);
        ConcurrencyHelper.ApplyExpectedRowVersion(db, item, r.RowVersion);

        var before = ToDtoLite(item);
        item.CategoryId = r.CategoryId;
        item.Sku = r.Sku.Trim().ToUpperInvariant();
        item.Name = r.Name.Trim();
        item.Description = r.Description?.Trim();
        item.ImageUrl = r.ImageUrl?.Trim();
        item.IsActive = r.IsActive;
        item.SortOrder = r.SortOrder;
        item.UpdatedAtUtc = clock.UtcNow;

        // Variants are never hard-deleted (historical orders reference them); missing ones are deactivated.
        foreach (var existing in item.Variants)
        {
            var input = r.Variants.FirstOrDefault(v => v.Id == existing.Id);
            if (input is null)
            {
                existing.IsActive = false;
                continue;
            }

            existing.Name = input.Name.Trim();
            existing.BasePrice = input.BasePrice;
            existing.SortOrder = input.SortOrder;
            existing.IsActive = input.IsActive;
        }

        foreach (var input in r.Variants.Where(v => v.Id is null || item.Variants.All(e => e.Id != v.Id)))
        {
            var variant = new MenuItemVariant { MenuItemId = item.Id, Name = input.Name.Trim(), BasePrice = input.BasePrice, SortOrder = input.SortOrder, IsActive = input.IsActive };
            db.MenuItemVariants.Add(variant);
        }

        var addOnIds = (r.AddOnIds ?? []).Distinct().ToHashSet();
        db.MenuItemAddOns.RemoveRange(item.AddOns.Where(a => !addOnIds.Contains(a.AddOnId)));
        foreach (var aid in addOnIds.Where(a => item.AddOns.All(x => x.AddOnId != a)))
        {
            db.MenuItemAddOns.Add(new MenuItemAddOn { MenuItemId = item.Id, AddOnId = aid });
        }

        audit.Record("menu.item_updated", "MenuItem", item.Id.ToString(), null, new { before, after = r with { RowVersion = null } });
        await db.SaveChangesAsync(ct);
        await InvalidatePublicMenusAsync(ct);
        return await GetItemAsync(item.Id, ct);
    }

    // ---------- Recipes ----------
    public async Task<IReadOnlyList<RecipeLineDto>> GetRecipeAsync(Guid? variantId, Guid? addOnId, CancellationToken ct)
    {
        var q = db.RecipeLines.AsNoTracking().Where(l => variantId != null ? l.VariantId == variantId : l.AddOnId == addOnId);
        return await q.Join(db.Ingredients, l => l.IngredientId, i => i.Id, (l, i) => new RecipeLineDto(i.Id, i.Code, i.Name, i.Unit, l.Quantity))
            .OrderBy(x => x.IngredientCode).ToListAsync(ct);
    }

    /// <summary>Replaces a recipe. Not retroactive: already-consumed orders keep their ledger entries.</summary>
    public async Task<IReadOnlyList<RecipeLineDto>> UpdateRecipeAsync(Guid? variantId, Guid? addOnId, UpdateRecipeRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        var v = new Validator();
        v.Must(r.Lines.Count <= 50, "lines", "Tối đa 50 nguyên liệu.");
        v.Must(r.Lines.Select(l => l.IngredientId).Distinct().Count() == r.Lines.Count, "lines", "Nguyên liệu bị trùng.");
        for (var i = 0; i < r.Lines.Count; i++)
        {
            v.Must(r.Lines[i].Quantity > 0 && r.Lines[i].Quantity <= 100_000, $"lines[{i}].quantity", "Định lượng phải > 0.");
        }

        v.ThrowIfInvalid();

        if (variantId is { } vid && !await db.MenuItemVariants.AnyAsync(x => x.Id == vid, ct))
        {
            throw new NotFoundException("MenuItemVariant", vid);
        }

        if (addOnId is { } aid && !await db.AddOns.AnyAsync(x => x.Id == aid, ct))
        {
            throw new NotFoundException("AddOn", aid);
        }

        var ingredientIds = r.Lines.Select(l => l.IngredientId).ToList();
        if (await db.Ingredients.CountAsync(i => ingredientIds.Contains(i.Id), ct) != ingredientIds.Count)
        {
            throw ValidationFailedException.For("lines", "Nguyên liệu không tồn tại.");
        }

        var existing = await db.RecipeLines.Where(l => variantId != null ? l.VariantId == variantId : l.AddOnId == addOnId).ToListAsync(ct);
        db.RecipeLines.RemoveRange(existing);
        foreach (var line in r.Lines)
        {
            db.RecipeLines.Add(new RecipeLine { VariantId = variantId, AddOnId = addOnId, IngredientId = line.IngredientId, Quantity = line.Quantity });
        }

        audit.Record("menu.recipe_updated", variantId != null ? "MenuItemVariant" : "AddOn", (variantId ?? addOnId).ToString(), null,
            new { before = existing.Select(e => new { e.IngredientId, e.Quantity }), after = r.Lines });
        await db.SaveChangesAsync(ct);
        return await GetRecipeAsync(variantId, addOnId, ct);
    }

    // ---------- Branch menu ----------
    public async Task<IReadOnlyList<BranchMenuItemDto>> GetBranchMenuAsync(Guid branchId, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        var items = await db.MenuItems.AsNoTracking().Include(i => i.Category).Include(i => i.Variants).AsSplitQuery()
            .OrderBy(i => i.Category!.SortOrder).ThenBy(i => i.SortOrder).ToListAsync(ct);
        var availability = await db.BranchMenuItems.AsNoTracking().Where(b => b.BranchId == branchId).ToDictionaryAsync(b => b.MenuItemId, ct);
        var prices = await db.BranchVariantPrices.AsNoTracking().Where(p => p.BranchId == branchId).ToDictionaryAsync(p => p.VariantId, p => p.Price, ct);

        return items.Select(i => new BranchMenuItemDto(i.Id, i.Name, i.Sku, i.Category!.Name, i.IsActive,
            availability.TryGetValue(i.Id, out var a) && a.IsAvailable,
            i.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).Select(v =>
            {
                decimal? bp = prices.TryGetValue(v.Id, out var p) ? p : null;
                return new BranchMenuVariantDto(v.Id, v.Name, v.BasePrice, bp, bp ?? v.BasePrice);
            }).ToList())).ToList();
    }

    public async Task<IReadOnlyList<BranchMenuItemDto>> UpdateBranchMenuAsync(Guid branchId, UpdateBranchMenuRequest r, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == branchId, ct) ?? throw new NotFoundException("Branch", branchId);
        var v = new Validator();
        v.Must(r.Items.Count <= 1000, "items", "Quá nhiều món.");
        foreach (var (input, idx) in r.Items.Select((x, i) => (x, i)))
        {
            foreach (var (vp, j) in (input.VariantPrices ?? []).Select((x, k) => (x, k)))
            {
                v.Must(vp.Price is null || (vp.Price >= 0 && vp.Price <= 100_000_000), $"items[{idx}].variantPrices[{j}].price", "Giá không hợp lệ.");
            }
        }

        v.ThrowIfInvalid();

        var itemIds = r.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var validItems = await db.MenuItems.Where(i => itemIds.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
        if (validItems.Count != itemIds.Count)
        {
            throw ValidationFailedException.For("items", "Món không tồn tại.");
        }

        var variantIds = r.Items.SelectMany(i => i.VariantPrices ?? []).Select(p => p.VariantId).Distinct().ToList();
        var variantOwners = await db.MenuItemVariants.Where(x => variantIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.MenuItemId, ct);

        var existingAvailability = await db.BranchMenuItems.Where(b => b.BranchId == branchId && itemIds.Contains(b.MenuItemId)).ToDictionaryAsync(b => b.MenuItemId, ct);
        var existingPrices = await db.BranchVariantPrices.Where(p => p.BranchId == branchId && variantIds.Contains(p.VariantId)).ToDictionaryAsync(p => p.VariantId, ct);
        var changes = new List<object>();

        foreach (var input in r.Items)
        {
            if (existingAvailability.TryGetValue(input.MenuItemId, out var row))
            {
                if (row.IsAvailable != input.IsAvailable)
                {
                    changes.Add(new { input.MenuItemId, availableBefore = row.IsAvailable, availableAfter = input.IsAvailable });
                }

                row.IsAvailable = input.IsAvailable;
                row.UpdatedAtUtc = clock.UtcNow;
            }
            else
            {
                db.BranchMenuItems.Add(new BranchMenuItem { BranchId = branchId, MenuItemId = input.MenuItemId, IsAvailable = input.IsAvailable, UpdatedAtUtc = clock.UtcNow });
                changes.Add(new { input.MenuItemId, availableAfter = input.IsAvailable });
            }

            foreach (var vp in input.VariantPrices ?? [])
            {
                if (!variantOwners.TryGetValue(vp.VariantId, out var owner) || owner != input.MenuItemId)
                {
                    throw ValidationFailedException.For("variantPrices", "Biến thể không thuộc món.");
                }

                existingPrices.TryGetValue(vp.VariantId, out var price);
                if (vp.Price is null)
                {
                    if (price is not null)
                    {
                        db.BranchVariantPrices.Remove(price);
                        changes.Add(new { vp.VariantId, priceBefore = price.Price, priceAfter = (decimal?)null });
                    }
                }
                else if (price is null)
                {
                    db.BranchVariantPrices.Add(new BranchVariantPrice { BranchId = branchId, VariantId = vp.VariantId, Price = vp.Price.Value });
                    changes.Add(new { vp.VariantId, priceAfter = vp.Price });
                }
                else if (price.Price != vp.Price.Value)
                {
                    changes.Add(new { vp.VariantId, priceBefore = price.Price, priceAfter = vp.Price });
                    price.Price = vp.Price.Value;
                }
            }
        }

        audit.Record("menu.branch_menu_updated", "Branch", branchId.ToString(), branchId, changes);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.PublicMenu(branch.Code), ct);
        return await GetBranchMenuAsync(branchId, ct);
    }

    private async Task ValidateItemAsync(UpsertMenuItemRequest r, Guid? id, CancellationToken ct)
    {
        var v = new Validator()
            .NotEmpty(r.CategoryId, "categoryId")
            .Required(r.Sku, "sku", 40)
            .Required(r.Name, "name", 150)
            .MaxLength(r.Description, "description", 1000)
            .MaxLength(r.ImageUrl, "imageUrl", 500)
            .Must(r.ImageUrl is null || r.ImageUrl.Length == 0 || Uri.TryCreate(r.ImageUrl, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http") || r.ImageUrl.StartsWith('/'),
                "imageUrl", "URL ảnh không hợp lệ.")
            .Must(r.Variants is { Count: > 0 and <= 10 }, "variants", "Món cần 1–10 kích cỡ/biến thể.");
        for (var i = 0; i < (r.Variants?.Count ?? 0); i++)
        {
            v.Required(r.Variants![i].Name, $"variants[{i}].name", 30)
             .Range(r.Variants[i].BasePrice, 0, 100_000_000, $"variants[{i}].basePrice");
        }

        v.ThrowIfInvalid();

        if (!await db.MenuCategories.AnyAsync(c => c.Id == r.CategoryId, ct))
        {
            throw ValidationFailedException.For("categoryId", "Danh mục không tồn tại.");
        }

        var sku = r.Sku.Trim().ToUpperInvariant();
        if (await db.MenuItems.AnyAsync(i => i.Sku == sku && i.Id != id, ct))
        {
            throw new ConflictException("sku_taken", "Mã món (SKU) đã tồn tại.");
        }

        var addOnIds = (r.AddOnIds ?? []).Distinct().ToList();
        if (addOnIds.Count > 0 && await db.AddOns.CountAsync(a => addOnIds.Contains(a.Id), ct) != addOnIds.Count)
        {
            throw ValidationFailedException.For("addOnIds", "Tùy chọn thêm không tồn tại.");
        }
    }

    private async Task InvalidatePublicMenusAsync(CancellationToken ct)
    {
        var codes = await db.Branches.AsNoTracking().Select(b => b.Code).ToListAsync(ct);
        foreach (var code in codes)
        {
            await cache.RemoveAsync(CacheKeys.PublicMenu(code), ct);
        }
    }

    private static object ToDtoLite(MenuItem i) => new
    {
        i.CategoryId,
        i.Sku,
        i.Name,
        i.IsActive,
        variants = i.Variants.Select(v => new { v.Id, v.Name, v.BasePrice, v.IsActive }).ToList(),
    };

    private static MenuItemDto ToDto(MenuItem i) => new(i.Id, i.CategoryId, i.Category?.Name ?? "", i.Sku, i.Name, i.Description, i.ImageUrl,
        i.IsActive, i.SortOrder,
        i.Variants.OrderBy(v => v.SortOrder).Select(v => new VariantDto(v.Id, v.Name, v.BasePrice, v.SortOrder, v.IsActive)).ToList(),
        i.AddOns.Select(a => a.AddOnId).ToList(), Convert.ToBase64String(i.RowVersion));
}
