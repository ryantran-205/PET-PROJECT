using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fnb.Application.Hr;

public sealed record AttendanceDto(Guid Id, Guid BranchId, Guid EmployeeId, string EmployeeName, DateOnly WorkDate, DateTime CheckInUtc,
    DateTime? CheckOutUtc, int? WorkedMinutes, int? PayableMinutes, string? RoundingRule, decimal PayMultiplier, bool IsLocked,
    string CheckInSource, string? CheckOutSource);

public sealed record CheckInRequest(Guid? EmployeeId, string? Source);

public sealed record CheckOutRequest(Guid? EmployeeId, string? Source);

public sealed record AdjustAttendanceRequest(DateTime CheckInUtc, DateTime? CheckOutUtc, string Reason);

public sealed record AttendanceQuery(Guid? BranchId, Guid? EmployeeId, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50);

public sealed class AttendanceService(
    IAppDbContext db,
    IClock clock,
    IAuditService audit,
    ICurrentUser currentUser,
    IUniqueConstraintDetector uniqueDetector,
    IOptions<AttendanceOptions> options)
{
    private AttendanceRoundingPolicy Policy => new(
        options.Value.RoundingIncrementMinutes,
        Enum.TryParse<RoundingMode>(options.Value.RoundingMode, true, out var m) ? m : RoundingMode.Floor);

    public async Task<AttendanceDto> CheckInAsync(CheckInRequest request, CancellationToken ct)
    {
        var employee = await ResolveEmployeeAsync(request.EmployeeId, ct);
        if (!employee.IsActive)
        {
            throw new BusinessRuleException("employee_inactive", "Nhân viên đã ngừng hoạt động.");
        }

        var open = await db.AttendanceRecords.AnyAsync(r => r.EmployeeId == employee.Id && r.CheckOutUtc == null, ct);
        if (open)
        {
            throw new BusinessRuleException("already_checked_in", "Nhân viên đang trong ca (chưa check-out).");
        }

        var now = clock.UtcNow;
        var branch = await db.Branches.AsNoTracking().FirstAsync(b => b.Id == employee.BranchId, ct);
        var localNow = BusinessTime.ToLocal(now, branch.TimeZoneId);
        var workDate = DateOnly.FromDateTime(localNow);

        // Link to a scheduled shift whose window (±2h) contains the check-in time; overnight shifts from yesterday included.
        var candidates = await db.ShiftAssignments.Include(a => a.WorkShift)
            .Where(a => a.EmployeeId == employee.Id && a.WorkDate >= workDate.AddDays(-1) && a.WorkDate <= workDate)
            .ToListAsync(ct);
        var matched = candidates.FirstOrDefault(a =>
        {
            var (start, end) = a.WorkShift!.WindowOn(a.WorkDate);
            return localNow >= start.AddHours(-2) && localNow <= end;
        });

        var record = new AttendanceRecord
        {
            BranchId = employee.BranchId,
            EmployeeId = employee.Id,
            CheckInUtc = now,
            CheckInSource = ParseSource(request.Source),
            CheckInIp = currentUser.IpAddress,
            WorkDate = matched?.WorkDate ?? workDate,
            ShiftAssignmentId = matched?.Id,
            PayMultiplier = matched?.WorkShift?.PayMultiplier ?? 1m,
            PerformedByUserId = currentUser.RequireUserId(),
        };
        db.AttendanceRecords.Add(record);
        audit.Record("attendance.check_in", "AttendanceRecord", record.Id.ToString(), record.BranchId, new { record.EmployeeId, record.ShiftAssignmentId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (uniqueDetector.IsUniqueViolation(ex))
        {
            // Concurrent double check-in caught by the filtered unique index.
            throw new BusinessRuleException("already_checked_in", "Nhân viên đang trong ca (chưa check-out).");
        }

        return ToDto(record, employee.FullName);
    }

    public async Task<AttendanceDto> CheckOutAsync(CheckOutRequest request, CancellationToken ct)
    {
        var employee = await ResolveEmployeeAsync(request.EmployeeId, ct);
        var record = await db.AttendanceRecords.FirstOrDefaultAsync(r => r.EmployeeId == employee.Id && r.CheckOutUtc == null, ct)
            ?? throw new BusinessRuleException("not_checked_in", "Nhân viên chưa check-in.");

        record.CheckOut(clock.UtcNow, ParseSource(request.Source), currentUser.IpAddress, Policy);
        audit.Record("attendance.check_out", "AttendanceRecord", record.Id.ToString(), record.BranchId,
            new { record.EmployeeId, record.WorkedMinutes, record.PayableMinutes });
        await db.SaveChangesAsync(ct);
        return ToDto(record, employee.FullName);
    }

    /// <summary>Manager correction with mandatory reason and audit. Locked (closed payroll) records cannot be changed.</summary>
    public async Task<AttendanceDto> AdjustAsync(Guid id, AdjustAttendanceRequest request, CancellationToken ct)
    {
        new Validator().Required(request.Reason, "reason", 500)
            .Must(request.CheckOutUtc is null || request.CheckOutUtc > request.CheckInUtc, "checkOutUtc", "Giờ ra phải sau giờ vào.")
            .Must(request.CheckInUtc <= clock.UtcNow.AddMinutes(5), "checkInUtc", "Không thể đặt giờ vào trong tương lai.")
            .ThrowIfInvalid();
        var record = await db.AttendanceRecords.Include(r => r.Employee).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("AttendanceRecord", id);
        currentUser.EnsureBranchAdmin(record.BranchId);
        if (record.IsLocked)
        {
            throw new BusinessRuleException("attendance_locked", "Bản ghi thuộc kỳ lương đã chốt.");
        }

        var before = new { record.CheckInUtc, record.CheckOutUtc, record.WorkedMinutes, record.PayableMinutes };
        record.CheckInUtc = DateTime.SpecifyKind(request.CheckInUtc, DateTimeKind.Utc);
        if (request.CheckOutUtc is { } outUtc)
        {
            record.CheckOutUtc = DateTime.SpecifyKind(outUtc, DateTimeKind.Utc);
            record.CheckOutSource ??= AttendanceSource.Manager;
            record.WorkedMinutes = AttendanceCalculator.WorkedMinutes(record.CheckInUtc, record.CheckOutUtc.Value);
            record.PayableMinutes = Policy.Apply(record.WorkedMinutes.Value);
            record.RoundingRule = Policy.Describe();
        }

        audit.Record("attendance.adjusted", "AttendanceRecord", record.Id.ToString(), record.BranchId,
            new { before, after = new { record.CheckInUtc, record.CheckOutUtc, record.WorkedMinutes, record.PayableMinutes }, request.Reason });
        await db.SaveChangesAsync(ct);
        return ToDto(record, record.Employee!.FullName);
    }

    public async Task<PagedResult<AttendanceDto>> ListAsync(AttendanceQuery query, CancellationToken ct)
    {
        var q = db.AttendanceRecords.AsNoTracking().AsQueryable();
        if (currentUser.IsBranchAdminOrAbove())
        {
            var branches = currentUser.ResolveBranchFilter(query.BranchId);
            if (branches is not null)
            {
                q = q.Where(r => branches.Contains(r.BranchId));
            }

            if (query.EmployeeId is { } eid)
            {
                q = q.Where(r => r.EmployeeId == eid);
            }
        }
        else
        {
            var uid = currentUser.RequireUserId();
            q = q.Where(r => r.Employee!.UserId == uid);
        }

        if (query.From is { } from)
        {
            q = q.Where(r => r.WorkDate >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(r => r.WorkDate <= to);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CheckInUtc).Skip(page.Skip).Take(page.SafePageSize)
            .Select(r => new AttendanceDto(r.Id, r.BranchId, r.EmployeeId, r.Employee!.FullName, r.WorkDate, r.CheckInUtc, r.CheckOutUtc,
                r.WorkedMinutes, r.PayableMinutes, r.RoundingRule, r.PayMultiplier, r.IsLocked, r.CheckInSource.ToString(),
                r.CheckOutSource == null ? null : r.CheckOutSource.ToString()))
            .ToListAsync(ct);
        return new PagedResult<AttendanceDto>(items, page.SafePage, page.SafePageSize, total);
    }

    /// <summary>Returns the current open attendance record (if any) of the signed-in employee.</summary>
    public async Task<AttendanceDto?> CurrentAsync(CancellationToken ct)
    {
        var uid = currentUser.RequireUserId();
        var r = await db.AttendanceRecords.AsNoTracking().Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Employee!.UserId == uid && x.CheckOutUtc == null, ct);
        return r is null ? null : ToDto(r, r.Employee!.FullName);
    }

    private async Task<Employee> ResolveEmployeeAsync(Guid? employeeId, CancellationToken ct)
    {
        var uid = currentUser.RequireUserId();
        Employee employee;
        if (employeeId is { } eid)
        {
            employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == eid, ct) ?? throw new NotFoundException("Employee", eid);
            if (employee.UserId != uid)
            {
                // Recording attendance for someone else requires manager permission on that branch.
                currentUser.EnsureBranchAdmin(employee.BranchId);
            }
        }
        else
        {
            employee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == uid, ct)
                ?? throw new BusinessRuleException("no_employee_profile", "Tài khoản chưa được liên kết hồ sơ nhân viên.");
        }

        currentUser.EnsureBranchAccess(employee.BranchId);
        return employee;
    }

    private static AttendanceSource ParseSource(string? s) =>
        Enum.TryParse<AttendanceSource>(s, true, out var v) && Enum.IsDefined(v) ? v : AttendanceSource.Web;

    private static AttendanceDto ToDto(AttendanceRecord r, string name) => new(r.Id, r.BranchId, r.EmployeeId, name, r.WorkDate, r.CheckInUtc,
        r.CheckOutUtc, r.WorkedMinutes, r.PayableMinutes, r.RoundingRule, r.PayMultiplier, r.IsLocked, r.CheckInSource.ToString(),
        r.CheckOutSource?.ToString());
}
