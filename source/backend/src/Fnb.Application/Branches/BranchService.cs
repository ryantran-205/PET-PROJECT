using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Branches;

public sealed record BranchDto(
    Guid Id, string Code, string Name, string Address, string? Phone, string TimeZoneId, bool IsActive, bool PublicOrderingEnabled,
    string? InvoiceHeader, string? InvoiceFooter, decimal TaxRatePercent, bool PricesIncludeTax, int RoundingUnit, bool AllowNegativeStock,
    IReadOnlyList<BranchTableDto> Tables, string RowVersion);

public sealed record BranchTableDto(Guid Id, string Name, string PublicCode, bool IsActive);

public sealed record CreateBranchRequest(
    string Code, string Name, string Address, string? Phone, string? TimeZoneId, string? InvoiceHeader, string? InvoiceFooter,
    decimal TaxRatePercent, bool PricesIncludeTax, int RoundingUnit);

public sealed record UpdateBranchRequest(
    string Name, string Address, string? Phone, string? TimeZoneId, bool IsActive, bool PublicOrderingEnabled,
    string? InvoiceHeader, string? InvoiceFooter, decimal TaxRatePercent, bool PricesIncludeTax, int RoundingUnit, bool AllowNegativeStock,
    string? RowVersion);

public sealed record CreateTableRequest(string Name);

public sealed record UpdateTableRequest(string Name, bool IsActive);

/// <summary>
/// Branch management. Only SuperAdmin creates branches or changes chain-wide settings (code, tax, rounding, stock policy, status).
/// BranchAdmin may update operational fields of their own branch (name, address, phone, invoice texts, public ordering toggle, tables).
/// Branches are never hard-deleted; they are deactivated.
/// </summary>
public sealed class BranchService(IAppDbContext db, IClock clock, IAuditService audit, ICurrentUser currentUser, ICacheService cache)
{
    public async Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct)
    {
        var q = db.Branches.AsNoTracking().Include(b => b.Tables).AsQueryable();
        if (!currentUser.IsSuperAdmin())
        {
            var ids = currentUser.BranchIds;
            q = q.Where(b => ids.Contains(b.Id));
        }

        var list = await q.OrderBy(b => b.Code).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<BranchDto> GetAsync(Guid id, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(id);
        var branch = await db.Branches.AsNoTracking().Include(b => b.Tables).FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("Branch", id);
        return ToDto(branch);
    }

    public async Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken ct)
    {
        currentUser.EnsureSuperAdmin();
        var tz = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "Asia/Ho_Chi_Minh" : request.TimeZoneId.Trim();
        new Validator()
            .Required(request.Code, "code", 30)
            .Must(request.Code is null || request.Code.All(c => char.IsLetterOrDigit(c) || c == '-'), "code", "Mã chỉ gồm chữ, số và dấu gạch ngang.")
            .Required(request.Name, "name", 150)
            .Required(request.Address, "address", 300)
            .MaxLength(request.Phone, "phone", 30)
            .Must(BusinessTime.IsValidTimeZone(tz), "timeZoneId", "Múi giờ không hợp lệ.")
            .Range(request.TaxRatePercent, 0, 100, "taxRatePercent")
            .Range(request.RoundingUnit, 1, 10_000, "roundingUnit")
            .MaxLength(request.InvoiceHeader, "invoiceHeader", 300)
            .MaxLength(request.InvoiceFooter, "invoiceFooter", 300)
            .ThrowIfInvalid();

        var code = request.Code!.Trim().ToUpperInvariant();
        if (await db.Branches.AnyAsync(b => b.Code == code, ct))
        {
            throw new ConflictException("branch_code_taken", "Mã chi nhánh đã tồn tại.");
        }

        var org = await db.Organizations.OrderBy(o => o.CreatedAtUtc).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("organization_missing", "Chưa cấu hình tổ chức.");

        var branch = new Branch
        {
            OrganizationId = org.Id,
            Code = code,
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Phone = request.Phone?.Trim(),
            TimeZoneId = tz,
            InvoiceHeader = request.InvoiceHeader,
            InvoiceFooter = request.InvoiceFooter,
            TaxRatePercent = request.TaxRatePercent,
            PricesIncludeTax = request.PricesIncludeTax,
            RoundingUnit = request.RoundingUnit,
            CreatedAtUtc = clock.UtcNow,
        };
        db.Branches.Add(branch);
        audit.Record("branch.created", "Branch", branch.Id.ToString(), branch.Id, new { branch.Code, branch.Name });
        await db.SaveChangesAsync(ct);
        return ToDto(branch);
    }

    public async Task<BranchDto> UpdateAsync(Guid id, UpdateBranchRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(id);
        var tz = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "Asia/Ho_Chi_Minh" : request.TimeZoneId.Trim();
        new Validator()
            .Required(request.Name, "name", 150)
            .Required(request.Address, "address", 300)
            .MaxLength(request.Phone, "phone", 30)
            .Must(BusinessTime.IsValidTimeZone(tz), "timeZoneId", "Múi giờ không hợp lệ.")
            .Range(request.TaxRatePercent, 0, 100, "taxRatePercent")
            .Range(request.RoundingUnit, 1, 10_000, "roundingUnit")
            .MaxLength(request.InvoiceHeader, "invoiceHeader", 300)
            .MaxLength(request.InvoiceFooter, "invoiceFooter", 300)
            .ThrowIfInvalid();

        var branch = await db.Branches.Include(b => b.Tables).FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("Branch", id);
        ConcurrencyHelper.ApplyExpectedRowVersion(db, branch, request.RowVersion);

        var chainWideChanged = branch.TaxRatePercent != request.TaxRatePercent
            || branch.PricesIncludeTax != request.PricesIncludeTax
            || branch.RoundingUnit != request.RoundingUnit
            || branch.AllowNegativeStock != request.AllowNegativeStock
            || branch.IsActive != request.IsActive
            || branch.TimeZoneId != tz;
        if (chainWideChanged && !currentUser.IsSuperAdmin())
        {
            throw new ForbiddenException("Chỉ Super Admin được thay đổi thuế, làm tròn, chính sách kho, múi giờ hoặc trạng thái chi nhánh.");
        }

        var before = ToDto(branch);
        branch.Name = request.Name.Trim();
        branch.Address = request.Address.Trim();
        branch.Phone = request.Phone?.Trim();
        branch.TimeZoneId = tz;
        branch.IsActive = request.IsActive;
        branch.PublicOrderingEnabled = request.PublicOrderingEnabled;
        branch.InvoiceHeader = request.InvoiceHeader;
        branch.InvoiceFooter = request.InvoiceFooter;
        branch.TaxRatePercent = request.TaxRatePercent;
        branch.PricesIncludeTax = request.PricesIncludeTax;
        branch.RoundingUnit = request.RoundingUnit;
        branch.AllowNegativeStock = request.AllowNegativeStock;
        branch.UpdatedAtUtc = clock.UtcNow;

        audit.Record("branch.updated", "Branch", branch.Id.ToString(), branch.Id, new { before = before with { Tables = [] }, after = ToDto(branch) with { Tables = [] } });
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.PublicMenu(branch.Code), ct);
        return ToDto(branch);
    }

    public async Task<BranchTableDto> AddTableAsync(Guid branchId, CreateTableRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        new Validator().Required(request.Name, "name", 50).ThrowIfInvalid();
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            throw new NotFoundException("Branch", branchId);
        }

        var table = new BranchTable { BranchId = branchId, Name = request.Name.Trim(), PublicCode = SecureTokens.Create(12) };
        db.BranchTables.Add(table);
        audit.Record("branch.table_created", "BranchTable", table.Id.ToString(), branchId, new { table.Name });
        await db.SaveChangesAsync(ct);
        return new BranchTableDto(table.Id, table.Name, table.PublicCode, table.IsActive);
    }

    public async Task<BranchTableDto> UpdateTableAsync(Guid branchId, Guid tableId, UpdateTableRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        new Validator().Required(request.Name, "name", 50).ThrowIfInvalid();
        var table = await db.BranchTables.FirstOrDefaultAsync(t => t.Id == tableId && t.BranchId == branchId, ct)
            ?? throw new NotFoundException("Table", tableId);
        table.Name = request.Name.Trim();
        table.IsActive = request.IsActive;
        audit.Record("branch.table_updated", "BranchTable", table.Id.ToString(), branchId, new { table.Name, table.IsActive });
        await db.SaveChangesAsync(ct);
        return new BranchTableDto(table.Id, table.Name, table.PublicCode, table.IsActive);
    }

    internal static BranchDto ToDto(Branch b) => new(
        b.Id, b.Code, b.Name, b.Address, b.Phone, b.TimeZoneId, b.IsActive, b.PublicOrderingEnabled, b.InvoiceHeader, b.InvoiceFooter,
        b.TaxRatePercent, b.PricesIncludeTax, b.RoundingUnit, b.AllowNegativeStock,
        b.Tables.OrderBy(t => t.Name).Select(t => new BranchTableDto(t.Id, t.Name, t.PublicCode, t.IsActive)).ToList(),
        Convert.ToBase64String(b.RowVersion));
}

public static class CacheKeys
{
    public static string PublicMenu(string branchCode) => $"menu:public:{branchCode.ToUpperInvariant()}";
}

public static class ConcurrencyHelper
{
    /// <summary>When the client sends the row version it loaded, EF compares it on update → 409 if someone else changed the row.</summary>
    public static void ApplyExpectedRowVersion<T>(IAppDbContext db, T entity, string? rowVersion)
        where T : class, IHasRowVersion
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw ValidationFailedException.For("rowVersion", "rowVersion không hợp lệ.");
        }

        if (db is DbContext ctx)
        {
            ctx.Entry(entity).Property(e => e.RowVersion).OriginalValue = expected;
        }
    }
}
