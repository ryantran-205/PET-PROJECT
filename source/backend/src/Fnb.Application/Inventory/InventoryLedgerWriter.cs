using Fnb.Application.Abstractions;
using Fnb.Domain.Common;
using Fnb.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Inventory;

public sealed record StockMovement(Guid IngredientId, decimal QuantityChange, string ReferenceKey);

/// <summary>
/// Applies stock movements atomically and writes immutable ledger entries. MUST be called inside a DB transaction.
/// Uses conditional set-based updates (UPDATE … WHERE QuantityOnHand + change >= 0) so that concurrent consumers
/// can never drive the balance negative, regardless of isolation level.
/// </summary>
public sealed class InventoryLedgerWriter(IAppDbContext db, IClock clock, IUniqueConstraintDetector uniqueDetector)
{
    public async Task<IReadOnlyList<InventoryLedgerEntry>> ApplyAsync(
        Guid branchId,
        IReadOnlyList<StockMovement> movements,
        LedgerEntryType entryType,
        string sourceType,
        Guid sourceId,
        string? reason,
        Guid? userId,
        bool allowNegative,
        CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Inventory movements must run inside a transaction.");
        }

        var now = clock.UtcNow;
        var entries = new List<InventoryLedgerEntry>();
        var shortfalls = new Dictionary<string, object?>();

        // Idempotency: skip movements whose reference key already exists (e.g. message redelivery).
        var keys = movements.Select(m => m.ReferenceKey).ToList();
        var existingKeys = (await db.InventoryLedgerEntries.AsNoTracking().Where(e => keys.Contains(e.ReferenceKey))
            .Select(e => e.ReferenceKey).ToListAsync(ct)).ToHashSet();

        // Deterministic lock order (by ingredient id) to minimise deadlocks between concurrent transactions.
        foreach (var m in movements.Where(m => !existingKeys.Contains(m.ReferenceKey)).OrderBy(m => m.IngredientId))
        {
            if (m.QuantityChange == 0)
            {
                continue;
            }

            await EnsureRowAsync(branchId, m.IngredientId, now, ct);
            var change = m.QuantityChange;
            int affected;
            if (change < 0 && !allowNegative)
            {
                affected = await db.BranchInventories
                    .Where(b => b.BranchId == branchId && b.IngredientId == m.IngredientId && b.QuantityOnHand + change >= 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand + change).SetProperty(b => b.UpdatedAtUtc, now), ct);
            }
            else
            {
                affected = await db.BranchInventories
                    .Where(b => b.BranchId == branchId && b.IngredientId == m.IngredientId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand + change).SetProperty(b => b.UpdatedAtUtc, now), ct);
            }

            if (affected == 0)
            {
                var onHand = await db.BranchInventories.AsNoTracking()
                    .Where(b => b.BranchId == branchId && b.IngredientId == m.IngredientId).Select(b => b.QuantityOnHand).FirstOrDefaultAsync(ct);
                shortfalls[m.IngredientId.ToString()] = new { required = -change, onHand };
                continue;
            }

            var balance = await db.BranchInventories.AsNoTracking()
                .Where(b => b.BranchId == branchId && b.IngredientId == m.IngredientId).Select(b => b.QuantityOnHand).FirstAsync(ct);
            var entry = new InventoryLedgerEntry
            {
                BranchId = branchId,
                IngredientId = m.IngredientId,
                EntryType = entryType,
                QuantityChange = change,
                BalanceAfter = balance,
                SourceType = sourceType,
                SourceId = sourceId,
                ReferenceKey = m.ReferenceKey,
                Reason = reason,
                CreatedByUserId = userId,
                CreatedAtUtc = now,
            };
            db.InventoryLedgerEntries.Add(entry);
            entries.Add(entry);
        }

        if (shortfalls.Count > 0)
        {
            // Caller's transaction is rolled back by disposing it without commit.
            throw new BusinessRuleException("insufficient_stock", "Không đủ tồn kho cho một số nguyên liệu.", shortfalls);
        }

        return entries;
    }

    private async Task EnsureRowAsync(Guid branchId, Guid ingredientId, DateTime now, CancellationToken ct)
    {
        var exists = await db.BranchInventories.AsNoTracking().AnyAsync(b => b.BranchId == branchId && b.IngredientId == ingredientId, ct);
        if (exists)
        {
            return;
        }

        var row = new BranchInventory { BranchId = branchId, IngredientId = ingredientId, QuantityOnHand = 0, MinThreshold = 0, UpdatedAtUtc = now };
        db.BranchInventories.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (uniqueDetector.IsUniqueViolation(ex))
        {
            // Created concurrently by another transaction — fine.
        }
        finally
        {
            if (db is DbContext ctx)
            {
                ctx.Entry(row).State = EntityState.Detached;
            }
        }
    }
}
