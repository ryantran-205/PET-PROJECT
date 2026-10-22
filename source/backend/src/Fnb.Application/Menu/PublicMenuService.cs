using Fnb.Application.Abstractions;
using Fnb.Application.Branches;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fnb.Application.Menu;

public sealed record PublicVariantDto(Guid Id, string Name, decimal Price);

public sealed record PublicAddOnDto(Guid Id, string Name, decimal Price);

public sealed record PublicMenuItemDto(Guid Id, Guid CategoryId, string Name, string? Description, string? ImageUrl,
    IReadOnlyList<PublicVariantDto> Variants, IReadOnlyList<PublicAddOnDto> AddOns);

public sealed record PublicCategoryDto(Guid Id, string Name, int SortOrder);

public sealed record PublicBranchDto(string Code, string Name, string Address, bool OrderingEnabled, string Currency, string? TableName);

public sealed record PublicMenuDto(PublicBranchDto Branch, IReadOnlyList<PublicCategoryDto> Categories, IReadOnlyList<PublicMenuItemDto> Items, DateTime GeneratedAtUtc);

/// <summary>
/// Public (anonymous) menu. Only exposes items sold at the branch with public prices — never costs, recipes or stock.
/// Cached per branch with TTL; falls back to the database if the cache is unavailable.
/// </summary>
public sealed class PublicMenuService(
    IAppDbContext db,
    ICacheService cache,
    IClock clock,
    IOptions<CacheOptions> cacheOptions,
    ILogger<PublicMenuService> logger)
{
    public async Task<PublicMenuDto> GetAsync(string branchCode, string? tableCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branchCode) || branchCode.Length > 30)
        {
            throw new NotFoundException("Branch");
        }

        var key = CacheKeys.PublicMenu(branchCode);
        var menu = await cache.GetAsync<PublicMenuDto>(key, ct);
        if (menu is null)
        {
            menu = await BuildAsync(branchCode.Trim().ToUpperInvariant(), ct);
            await cache.SetAsync(key, menu, TimeSpan.FromSeconds(cacheOptions.Value.PublicMenuSeconds), ct);
        }
        else
        {
            logger.LogDebug("Public menu cache hit for {BranchCode}", branchCode);
        }

        if (!string.IsNullOrWhiteSpace(tableCode))
        {
            var tableName = await db.BranchTables.AsNoTracking()
                .Where(t => t.PublicCode == tableCode && t.IsActive && t.BranchId == db.Branches.Where(b => b.Code == menu.Branch.Code).Select(b => b.Id).FirstOrDefault())
                .Select(t => t.Name).FirstOrDefaultAsync(ct);
            menu = menu with { Branch = menu.Branch with { TableName = tableName } };
        }

        return menu;
    }

    private async Task<PublicMenuDto> BuildAsync(string code, CancellationToken ct)
    {
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code && b.IsActive, ct)
            ?? throw new NotFoundException("Branch");
        var currency = await db.Organizations.AsNoTracking().Where(o => o.Id == branch.OrganizationId).Select(o => o.Currency).FirstOrDefaultAsync(ct) ?? "VND";

        var availableIds = db.BranchMenuItems.Where(b => b.BranchId == branch.Id && b.IsAvailable).Select(b => b.MenuItemId);
        var items = await db.MenuItems.AsNoTracking()
            .Include(i => i.Category).Include(i => i.Variants).Include(i => i.AddOns).ThenInclude(a => a.AddOn)
            .Where(i => i.IsActive && i.Category!.IsActive && availableIds.Contains(i.Id))
            .OrderBy(i => i.Category!.SortOrder).ThenBy(i => i.SortOrder).ThenBy(i => i.Name)
            .AsSplitQuery()
            .ToListAsync(ct);
        var prices = await db.BranchVariantPrices.AsNoTracking().Where(p => p.BranchId == branch.Id).ToDictionaryAsync(p => p.VariantId, p => p.Price, ct);

        var dtoItems = items
            .Select(i => new PublicMenuItemDto(i.Id, i.CategoryId, i.Name, i.Description, i.ImageUrl,
                i.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder)
                    .Select(v => new PublicVariantDto(v.Id, v.Name, prices.TryGetValue(v.Id, out var p) ? p : v.BasePrice)).ToList(),
                i.AddOns.Where(a => a.AddOn!.IsActive).OrderBy(a => a.AddOn!.Name)
                    .Select(a => new PublicAddOnDto(a.AddOnId, a.AddOn!.Name, a.AddOn.Price)).ToList()))
            .Where(i => i.Variants.Count > 0)
            .ToList();
        var categories = items.Select(i => i.Category!).DistinctBy(c => c.Id).OrderBy(c => c.SortOrder)
            .Select(c => new PublicCategoryDto(c.Id, c.Name, c.SortOrder)).ToList();

        return new PublicMenuDto(
            new PublicBranchDto(branch.Code, branch.Name, branch.Address, branch.PublicOrderingEnabled, currency, null),
            categories, dtoItems, clock.UtcNow);
    }
}
