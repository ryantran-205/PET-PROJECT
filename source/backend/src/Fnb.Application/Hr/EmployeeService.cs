using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Hr;

public sealed record EmployeeDto(Guid Id, Guid BranchId, string EmployeeCode, string FullName, string? Phone, string? Email, string Position,
    decimal HourlyRate, bool IsActive, Guid? UserId, string RowVersion);

public sealed record CreateEmployeeRequest(string EmployeeCode, string FullName, string? Phone, string? Email, string? Position, decimal HourlyRate, Guid? UserId);

public sealed record UpdateEmployeeRequest(string FullName, string? Phone, string? Email, string? Position, decimal HourlyRate, bool IsActive, Guid? UserId, string? RowVersion);

public sealed record EmployeeQuery(string? Search, bool? IsActive, int Page = 1, int PageSize = 50);

public sealed class EmployeeService(IAppDbContext db, IClock clock, IAuditService audit, ICurrentUser currentUser)
{
    public async Task<PagedResult<EmployeeDto>> ListAsync(Guid branchId, EmployeeQuery query, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        var q = db.Employees.AsNoTracking().Where(e => e.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(e => e.FullName.Contains(s) || e.EmployeeCode.Contains(s));
        }

        if (query.IsActive is { } active)
        {
            q = q.Where(e => e.IsActive == active);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(e => e.EmployeeCode).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<EmployeeDto>(items.Select(ToDto).ToList(), page.SafePage, page.SafePageSize, total);
    }

    public async Task<EmployeeDto> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Employee", id);
        if (e.UserId != currentUser.UserId)
        {
            currentUser.EnsureBranchAdmin(e.BranchId);
        }

        return ToDto(e);
    }

    public async Task<EmployeeDto> CreateAsync(Guid branchId, CreateEmployeeRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        Validate(request.FullName, request.Phone, request.Email, request.Position, request.HourlyRate)
            .Required(request.EmployeeCode, "employeeCode", 30)
            .ThrowIfInvalid();
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            throw new NotFoundException("Branch", branchId);
        }

        var code = request.EmployeeCode.Trim().ToUpperInvariant();
        if (await db.Employees.AnyAsync(e => e.EmployeeCode == code, ct))
        {
            throw new ConflictException("employee_code_taken", "Mã nhân viên đã tồn tại.");
        }

        await ValidateUserLinkAsync(request.UserId, branchId, null, ct);
        var employee = new Employee
        {
            BranchId = branchId,
            EmployeeCode = code,
            FullName = request.FullName.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Position = string.IsNullOrWhiteSpace(request.Position) ? "Staff" : request.Position.Trim(),
            HourlyRate = request.HourlyRate,
            UserId = request.UserId,
            CreatedAtUtc = clock.UtcNow,
        };
        db.Employees.Add(employee);
        audit.Record("employee.created", "Employee", employee.Id.ToString(), branchId, new { employee.EmployeeCode, employee.FullName, employee.HourlyRate });
        await db.SaveChangesAsync(ct);
        return ToDto(employee);
    }

    public async Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Employee", id);
        currentUser.EnsureBranchAdmin(employee.BranchId);
        Validate(request.FullName, request.Phone, request.Email, request.Position, request.HourlyRate).ThrowIfInvalid();
        await ValidateUserLinkAsync(request.UserId, employee.BranchId, employee.Id, ct);
        Branches.ConcurrencyHelper.ApplyExpectedRowVersion(db, employee, request.RowVersion);

        var before = ToDto(employee);
        employee.FullName = request.FullName.Trim();
        employee.Phone = request.Phone?.Trim();
        employee.Email = request.Email?.Trim();
        employee.Position = string.IsNullOrWhiteSpace(request.Position) ? "Staff" : request.Position.Trim();
        employee.HourlyRate = request.HourlyRate;
        employee.IsActive = request.IsActive;
        employee.UserId = request.UserId;
        employee.UpdatedAtUtc = clock.UtcNow;
        audit.Record("employee.updated", "Employee", employee.Id.ToString(), employee.BranchId, new { before, after = ToDto(employee) });
        await db.SaveChangesAsync(ct);
        return ToDto(employee);
    }

    private async Task ValidateUserLinkAsync(Guid? userId, Guid branchId, Guid? employeeId, CancellationToken ct)
    {
        if (userId is not { } uid)
        {
            return;
        }

        var assigned = await db.UserBranchAssignments.AnyAsync(a => a.UserId == uid && a.BranchId == branchId, ct);
        if (!assigned)
        {
            throw ValidationFailedException.For("userId", "Tài khoản phải được gán cho chi nhánh của nhân viên.");
        }

        if (await db.Employees.AnyAsync(e => e.UserId == uid && e.Id != employeeId, ct))
        {
            throw ValidationFailedException.For("userId", "Tài khoản đã được liên kết với nhân viên khác.");
        }
    }

    private static Validator Validate(string fullName, string? phone, string? email, string? position, decimal hourlyRate) =>
        new Validator()
            .Required(fullName, "fullName", 150)
            .MaxLength(phone, "phone", 30)
            .MaxLength(email, "email", 200)
            .MaxLength(position, "position", 50)
            .Range(hourlyRate, 0, 10_000_000, "hourlyRate");

    internal static EmployeeDto ToDto(Employee e) => new(e.Id, e.BranchId, e.EmployeeCode, e.FullName, e.Phone, e.Email, e.Position,
        e.HourlyRate, e.IsActive, e.UserId, Convert.ToBase64String(e.RowVersion));
}
