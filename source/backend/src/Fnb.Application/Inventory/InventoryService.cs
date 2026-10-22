using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Inventory;

public sealed record IngredientDto(Guid Id, string Code, string Name, string Unit, bool IsActive);

public sealed record UpsertIngredientRequest(string Code, string Name, string Unit, bool IsActive = true);

public sealed record BranchStockDto(Guid IngredientId, string Code, string Name, string Unit, decimal QuantityOnHand, decimal MinThreshold, bool IsLow, DateTime UpdatedAtUtc);

public sealed record ConfigureBranchIngredientRequest(Guid IngredientId, decimal MinThreshold);

public sealed record StockLineInput(Guid IngredientId, decimal Quantity, decimal? UnitCost);

public sealed record StockDocumentRequest(string Reason, string? Supplier, IReadOnlyList<StockLineInput> Lines);

public sealed record AdjustmentLineInput(Guid IngredientId, decimal QuantityChange);

public sealed record AdjustmentRequest(string Reason, IReadOnlyList<AdjustmentLineInput> Lines);

public sealed record StocktakeLineInput(Guid IngredientId, decimal CountedQuantity);

public sealed record StocktakeRequest(string Reason, IReadOnlyList<StocktakeLineInput> Lines);

public sealed record InventoryDocumentDto(Guid Id, string DocumentNo, string Type, string Reason, string? Supplier, DateTime CreatedAtUtc,
    IReadOnlyList<InventoryDocumentLineDto> Lines);

public sealed record InventoryDocumentLineDto(Guid IngredientId, decimal QuantityChange, decimal? CountedQuantity, decimal? UnitCost, decimal BalanceAfter);

public sealed record LedgerEntryDto(Guid Id, Guid IngredientId, string IngredientCode, string IngredientName, string Unit, string EntryType,
    decimal QuantityChange, decimal BalanceAfter, string SourceType, Guid SourceId, string? Reason, DateTime CreatedAtUtc);

public sealed record LedgerQuery(Guid? IngredientId, DateTime? FromUtc, DateTime? ToUtc, string? EntryType, int Page = 1, int PageSize = 50);

public sealed class InventoryService(
    IAppDbContext db,
    IClock clock,
    IAuditService audit,
    ICurrentUser currentUser,
    ISequenceAllocator sequences,
    InventoryLedgerWriter ledger,
    IUniqueConstraintDetector uniqueDetector)
{
    // ---------- Ingredient catalogue (chain-wide) ----------
    public async Task<IReadOnlyList<IngredientDto>> ListIngredientsAsync(CancellationToken ct) =>
        await db.Ingredients.AsNoTracking().OrderBy(i => i.Code).Select(i => new IngredientDto(i.Id, i.Code, i.Name, i.Unit, i.IsActive)).ToListAsync(ct);

    public async Task<IngredientDto> CreateIngredientAsync(UpsertIngredientRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        ValidateIngredient(r);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Ingredients.AnyAsync(i => i.Code == code, ct))
        {
            throw new ConflictException("ingredient_code_taken", "Mã nguyên liệu đã tồn tại.");
        }

        var i = new Ingredient { Code = code, Name = r.Name.Trim(), Unit = r.Unit.Trim(), IsActive = r.IsActive };
        db.Ingredients.Add(i);
        audit.Record("inventory.ingredient_created", "Ingredient", i.Id.ToString(), null, r);
        await db.SaveChangesAsync(ct);
        return new IngredientDto(i.Id, i.Code, i.Name, i.Unit, i.IsActive);
    }

    public async Task<IngredientDto> UpdateIngredientAsync(Guid id, UpsertIngredientRequest r, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        ValidateIngredient(r);
        var i = await db.Ingredients.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Ingredient", id);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Ingredients.AnyAsync(x => x.Code == code && x.Id != id, ct))
        {
            throw new ConflictException("ingredient_code_taken", "Mã nguyên liệu đã tồn tại.");
        }

        if (i.Unit != r.Unit.Trim() && await db.InventoryLedgerEntries.AnyAsync(e => e.IngredientId == id, ct))
        {
            throw new BusinessRuleException("unit_locked", "Không thể đổi đơn vị của nguyên liệu đã phát sinh giao dịch kho.");
        }

        var before = new { i.Code, i.Name, i.Unit, i.IsActive };
        i.Code = code;
        i.Name = r.Name.Trim();
        i.Unit = r.Unit.Trim();
        i.IsActive = r.IsActive;
        audit.Record("inventory.ingredient_updated", "Ingredient", i.Id.ToString(), null, new { before, after = r });
        await db.SaveChangesAsync(ct);
        return new IngredientDto(i.Id, i.Code, i.Name, i.Unit, i.IsActive);
    }

    // ---------- Branch stock ----------
    public async Task<IReadOnlyList<BranchStockDto>> ListStockAsync(Guid branchId, bool lowOnly, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        var q = db.BranchInventories.AsNoTracking().Where(b => b.BranchId == branchId);
        if (lowOnly)
        {
            q = q.Where(b => b.QuantityOnHand < b.MinThreshold);
        }

        return await q.OrderBy(b => b.Ingredient!.Code)
            .Select(b => new BranchStockDto(b.IngredientId, b.Ingredient!.Code, b.Ingredient.Name, b.Ingredient.Unit, b.QuantityOnHand, b.MinThreshold,
                b.QuantityOnHand < b.MinThreshold, b.UpdatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<BranchStockDto> ConfigureIngredientAsync(Guid branchId, ConfigureBranchIngredientRequest r, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        new Validator().NotEmpty(r.IngredientId, "ingredientId").Range(r.MinThreshold, 0, 100_000_000, "minThreshold").ThrowIfInvalid();
        var ingredient = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == r.IngredientId, ct) ?? throw ValidationFailedException.For("ingredientId", "Nguyên liệu không tồn tại.");
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            throw new NotFoundException("Branch", branchId);
        }

        var row = await db.BranchInventories.FirstOrDefaultAsync(b => b.BranchId == branchId && b.IngredientId == r.IngredientId, ct);
        if (row is null)
        {
            row = new BranchInventory { BranchId = branchId, IngredientId = r.IngredientId, MinThreshold = r.MinThreshold, UpdatedAtUtc = clock.UtcNow };
            db.BranchInventories.Add(row);
        }
        else
        {
            row.MinThreshold = r.MinThreshold;
            row.UpdatedAtUtc = clock.UtcNow;
        }

        audit.Record("inventory.threshold_configured", "BranchInventory", r.IngredientId.ToString(), branchId, r);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (uniqueDetector.IsUniqueViolation(ex))
        {
            throw new ConflictException("concurrent_update", "Dữ liệu vừa được cập nhật bởi người khác, vui lòng thử lại.");
        }

        return new BranchStockDto(ingredient.Id, ingredient.Code, ingredient.Name, ingredient.Unit, row.QuantityOnHand, row.MinThreshold, row.IsLow, row.UpdatedAtUtc);
    }

    // ---------- Documents ----------
    public Task<InventoryDocumentDto> ReceiveAsync(Guid branchId, StockDocumentRequest r, CancellationToken ct) =>
        CreateDocumentAsync(branchId, InventoryDocumentType.Receipt, r.Reason, r.Supplier,
            ValidateLines(r.Lines).Select(l => (l.IngredientId, l.Quantity, (decimal?)null, l.UnitCost)).ToList(), ct);

    public Task<InventoryDocumentDto> IssueAsync(Guid branchId, StockDocumentRequest r, CancellationToken ct) =>
        CreateDocumentAsync(branchId, InventoryDocumentType.Issue, r.Reason, null,
            ValidateLines(r.Lines).Select(l => (l.IngredientId, -l.Quantity, (decimal?)null, l.UnitCost)).ToList(), ct);

    public Task<InventoryDocumentDto> AdjustAsync(Guid branchId, AdjustmentRequest r, CancellationToken ct)
    {
        var v = new Validator().Must(r.Lines is { Count: > 0 and <= 200 }, "lines", "Cần 1–200 dòng.");
        for (var i = 0; i < (r.Lines?.Count ?? 0); i++)
        {
            v.Must(r.Lines![i].QuantityChange != 0 && Math.Abs(r.Lines[i].QuantityChange) <= 100_000_000, $"lines[{i}].quantityChange", "Số lượng điều chỉnh phải khác 0.");
        }

        v.Must(r.Lines is null || r.Lines.Select(l => l.IngredientId).Distinct().Count() == r.Lines.Count, "lines", "Nguyên liệu bị trùng.");
        v.ThrowIfInvalid();
        return CreateDocumentAsync(branchId, InventoryDocumentType.Adjustment, r.Reason, null,
            r.Lines!.Select(l => (l.IngredientId, l.QuantityChange, (decimal?)null, (decimal?)null)).ToList(), ct);
    }

    public async Task<InventoryDocumentDto> StocktakeAsync(Guid branchId, StocktakeRequest r, CancellationToken ct)
    {
        var v = new Validator().Must(r.Lines is { Count: > 0 and <= 500 }, "lines", "Cần 1–500 dòng.");
        for (var i = 0; i < (r.Lines?.Count ?? 0); i++)
        {
            v.Range(r.Lines![i].CountedQuantity, 0, 100_000_000, $"lines[{i}].countedQuantity");
        }

        v.Must(r.Lines is null || r.Lines.Select(l => l.IngredientId).Distinct().Count() == r.Lines.Count, "lines", "Nguyên liệu bị trùng.");
        v.ThrowIfInvalid();
        return await CreateDocumentAsync(branchId, InventoryDocumentType.Stocktake, r.Reason, null,
            r.Lines!.Select(l => (l.IngredientId, 0m, (decimal?)l.CountedQuantity, (decimal?)null)).ToList(), ct);
    }

    private async Task<InventoryDocumentDto> CreateDocumentAsync(
        Guid branchId,
        InventoryDocumentType type,
        string reason,
        string? supplier,
        List<(Guid IngredientId, decimal Change, decimal? Counted, decimal? UnitCost)> lines,
        CancellationToken ct)
    {
        // Receipts/issues can be done by staff on duty; adjustments and stocktakes require branch admin.
        if (type is InventoryDocumentType.Adjustment or InventoryDocumentType.Stocktake)
        {
            currentUser.EnsureBranchAdmin(branchId);
        }
        else
        {
            currentUser.EnsureBranchAccess(branchId);
        }

        new Validator().Required(reason, "reason", 500).MaxLength(supplier, "supplier", 200).ThrowIfInvalid();
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, ct) ?? throw new NotFoundException("Branch", branchId);
        var ingredientIds = lines.Select(l => l.IngredientId).Distinct().ToList();
        var ingredients = await db.Ingredients.AsNoTracking().Where(i => ingredientIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        if (ingredients.Count != ingredientIds.Count)
        {
            throw ValidationFailedException.For("lines", "Nguyên liệu không tồn tại.");
        }

        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var businessDate = BusinessTime.BusinessDate(now, branch.TimeZoneId);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var seq = await sequences.NextAsync("inventory-document", branchId, businessDate, ct);
        var prefix = type switch
        {
            InventoryDocumentType.Receipt => "NK",
            InventoryDocumentType.Issue => "XK",
            InventoryDocumentType.Adjustment => "DC",
            _ => "KK",
        };
        var doc = new InventoryDocument
        {
            BranchId = branchId,
            DocumentNo = $"{prefix}-{branch.Code}-{businessDate:yyyyMMdd}-{seq:D4}",
            Type = type,
            Reason = reason.Trim(),
            Supplier = supplier?.Trim(),
            CreatedByUserId = userId,
            CreatedAtUtc = now,
        };

        var movements = new List<StockMovement>();
        foreach (var l in lines)
        {
            var change = l.Change;
            if (type == InventoryDocumentType.Stocktake)
            {
                // Lock the row first (no-op update) so the counted delta is computed against a stable balance.
                await db.BranchInventories.Where(b => b.BranchId == branchId && b.IngredientId == l.IngredientId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand), ct);
                var current = await db.BranchInventories.AsNoTracking().Where(b => b.BranchId == branchId && b.IngredientId == l.IngredientId)
                    .Select(b => (decimal?)b.QuantityOnHand).FirstOrDefaultAsync(ct) ?? 0m;
                change = l.Counted!.Value - current;
            }

            doc.Lines.Add(new InventoryDocumentLine { DocumentId = doc.Id, IngredientId = l.IngredientId, QuantityChange = change, CountedQuantity = l.Counted, UnitCost = l.UnitCost });
            movements.Add(new StockMovement(l.IngredientId, change, $"doc:{doc.Id}:{l.IngredientId}"));
        }

        db.InventoryDocuments.Add(doc);
        var entryType = type switch
        {
            InventoryDocumentType.Receipt => LedgerEntryType.Receipt,
            InventoryDocumentType.Issue => LedgerEntryType.Issue,
            InventoryDocumentType.Adjustment => LedgerEntryType.Adjustment,
            _ => LedgerEntryType.Stocktake,
        };
        var entries = await ledger.ApplyAsync(branchId, movements, entryType, "InventoryDocument", doc.Id, doc.Reason, userId, branch.AllowNegativeStock, ct);
        audit.Record($"inventory.{type.ToString().ToLowerInvariant()}", "InventoryDocument", doc.Id.ToString(), branchId,
            new { doc.DocumentNo, doc.Reason, lines = doc.Lines.Select(x => new { x.IngredientId, x.QuantityChange, x.CountedQuantity }) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var balances = entries.ToDictionary(e => e.IngredientId, e => e.BalanceAfter);
        return new InventoryDocumentDto(doc.Id, doc.DocumentNo, doc.Type.ToString(), doc.Reason, doc.Supplier, doc.CreatedAtUtc,
            doc.Lines.Select(x => new InventoryDocumentLineDto(x.IngredientId, x.QuantityChange, x.CountedQuantity, x.UnitCost,
                balances.GetValueOrDefault(x.IngredientId))).ToList());
    }

    public async Task<PagedResult<InventoryDocumentDto>> ListDocumentsAsync(Guid branchId, int page, int pageSize, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        var p = new PageRequest(page, pageSize);
        var q = db.InventoryDocuments.AsNoTracking().Where(d => d.BranchId == branchId);
        var total = await q.CountAsync(ct);
        var docs = await q.OrderByDescending(d => d.CreatedAtUtc).Skip(p.Skip).Take(p.SafePageSize).Include(d => d.Lines).ToListAsync(ct);
        return new PagedResult<InventoryDocumentDto>(docs.Select(d => new InventoryDocumentDto(d.Id, d.DocumentNo, d.Type.ToString(), d.Reason, d.Supplier,
            d.CreatedAtUtc, d.Lines.Select(x => new InventoryDocumentLineDto(x.IngredientId, x.QuantityChange, x.CountedQuantity, x.UnitCost, 0)).ToList())).ToList(),
            p.SafePage, p.SafePageSize, total);
    }

    public async Task<PagedResult<LedgerEntryDto>> LedgerAsync(Guid branchId, LedgerQuery query, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        var q = db.InventoryLedgerEntries.AsNoTracking().Where(e => e.BranchId == branchId);
        if (query.IngredientId is { } iid)
        {
            q = q.Where(e => e.IngredientId == iid);
        }

        if (query.FromUtc is { } from)
        {
            q = q.Where(e => e.CreatedAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            q = q.Where(e => e.CreatedAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(query.EntryType) && Enum.TryParse<LedgerEntryType>(query.EntryType, true, out var et))
        {
            q = q.Where(e => e.EntryType == et);
        }

        var p = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.Id).Skip(p.Skip).Take(p.SafePageSize)
            .Join(db.Ingredients, e => e.IngredientId, i => i.Id, (e, i) => new LedgerEntryDto(e.Id, e.IngredientId, i.Code, i.Name, i.Unit,
                e.EntryType.ToString(), e.QuantityChange, e.BalanceAfter, e.SourceType, e.SourceId, e.Reason, e.CreatedAtUtc))
            .ToListAsync(ct);
        return new PagedResult<LedgerEntryDto>(items, p.SafePage, p.SafePageSize, total);
    }

    private static IReadOnlyList<StockLineInput> ValidateLines(IReadOnlyList<StockLineInput>? lines)
    {
        var v = new Validator().Must(lines is { Count: > 0 and <= 200 }, "lines", "Cần 1–200 dòng.");
        for (var i = 0; i < (lines?.Count ?? 0); i++)
        {
            v.Must(lines![i].Quantity > 0 && lines[i].Quantity <= 100_000_000, $"lines[{i}].quantity", "Số lượng phải > 0.");
            v.Must(lines[i].UnitCost is null || lines[i].UnitCost >= 0, $"lines[{i}].unitCost", "Đơn giá không hợp lệ.");
        }

        v.Must(lines is null || lines.Select(l => l.IngredientId).Distinct().Count() == lines.Count, "lines", "Nguyên liệu bị trùng.");
        v.ThrowIfInvalid();
        return lines!;
    }

    private static void ValidateIngredient(UpsertIngredientRequest r) =>
        new Validator().Required(r.Code, "code", 30).Required(r.Name, "name", 150).Required(r.Unit, "unit", 10).ThrowIfInvalid();
}
