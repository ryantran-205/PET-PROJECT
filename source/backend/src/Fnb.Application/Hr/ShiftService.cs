using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Hr;

public sealed record WorkShiftDto(Guid Id, Guid BranchId, string Name, TimeOnly StartTime, TimeOnly EndTime, decimal PayMultiplier,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, bool CrossesMidnight);

public sealed record UpsertWorkShiftRequest(string Name, TimeOnly StartTime, TimeOnly EndTime, decimal PayMultiplier, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive = true);

public sealed record ShiftAssignmentDto(Guid Id, Guid BranchId, Guid EmployeeId, string EmployeeName, Guid WorkShiftId, string ShiftName,
    DateOnly WorkDate, TimeOnly StartTime, TimeOnly EndTime, string? Note);

public sealed record CreateShiftAssignmentRequest(Guid EmployeeId, Guid WorkShiftId, DateOnly WorkDate, string? Note);

public sealed record ShiftAssignmentQuery(DateOnly From, DateOnly To, Guid? EmployeeId);

public sealed class ShiftService(IAppDbContext db, IClock clock, IAuditService audit, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<WorkShiftDto>> ListShiftsAsync(Guid branchId, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        var shifts = await db.WorkShifts.AsNoTracking().Where(s => s.BranchId == branchId).OrderBy(s => s.StartTime).ToListAsync(ct);
        return shifts.Select(ToDto).ToList();
    }

    public async Task<WorkShiftDto> CreateShiftAsync(Guid branchId, UpsertWorkShiftRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        ValidateShift(request);
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            throw new NotFoundException("Branch", branchId);
        }

        var shift = new WorkShift
        {
            BranchId = branchId,
            Name = request.Name.Trim(),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            PayMultiplier = request.PayMultiplier,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = request.IsActive,
        };
        db.WorkShifts.Add(shift);
        audit.Record("shift.created", "WorkShift", shift.Id.ToString(), branchId, ToDto(shift));
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    public async Task<WorkShiftDto> UpdateShiftAsync(Guid branchId, Guid shiftId, UpsertWorkShiftRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        ValidateShift(request);
        var shift = await db.WorkShifts.FirstOrDefaultAsync(s => s.Id == shiftId && s.BranchId == branchId, ct)
            ?? throw new NotFoundException("WorkShift", shiftId);
        var before = ToDto(shift);
        shift.Name = request.Name.Trim();
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.PayMultiplier = request.PayMultiplier;
        shift.EffectiveFrom = request.EffectiveFrom;
        shift.EffectiveTo = request.EffectiveTo;
        shift.IsActive = request.IsActive;
        audit.Record("shift.updated", "WorkShift", shift.Id.ToString(), branchId, new { before, after = ToDto(shift) });
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    public async Task<IReadOnlyList<ShiftAssignmentDto>> ListAssignmentsAsync(Guid branchId, ShiftAssignmentQuery query, CancellationToken ct)
    {
        currentUser.EnsureBranchAccess(branchId);
        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber > 62)
        {
            throw ValidationFailedException.For("to", "Khoảng ngày không hợp lệ (tối đa 62 ngày).");
        }

        var q = db.ShiftAssignments.AsNoTracking()
            .Where(a => a.BranchId == branchId && a.WorkDate >= query.From && a.WorkDate <= query.To);

        if (!currentUser.IsBranchAdminOrAbove())
        {
            // Staff only see their own schedule.
            var ownEmployeeId = await db.Employees.Where(e => e.UserId == currentUser.UserId && e.BranchId == branchId)
                .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            q = q.Where(a => a.EmployeeId == ownEmployeeId);
        }
        else if (query.EmployeeId is { } eid)
        {
            q = q.Where(a => a.EmployeeId == eid);
        }

        return await q.OrderBy(a => a.WorkDate).ThenBy(a => a.WorkShift!.StartTime)
            .Select(a => new ShiftAssignmentDto(a.Id, a.BranchId, a.EmployeeId, a.Employee!.FullName, a.WorkShiftId, a.WorkShift!.Name,
                a.WorkDate, a.WorkShift.StartTime, a.WorkShift.EndTime, a.Note))
            .ToListAsync(ct);
    }

    public async Task<ShiftAssignmentDto> AssignAsync(Guid branchId, CreateShiftAssignmentRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        new Validator()
            .NotEmpty(request.EmployeeId, "employeeId")
            .NotEmpty(request.WorkShiftId, "workShiftId")
            .MaxLength(request.Note, "note", 200)
            .ThrowIfInvalid();

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.BranchId == branchId, ct)
            ?? throw ValidationFailedException.For("employeeId", "Nhân viên không thuộc chi nhánh.");
        if (!employee.IsActive)
        {
            throw new BusinessRuleException("employee_inactive", "Nhân viên đã ngừng hoạt động.");
        }

        var shift = await db.WorkShifts.FirstOrDefaultAsync(s => s.Id == request.WorkShiftId && s.BranchId == branchId, ct)
            ?? throw ValidationFailedException.For("workShiftId", "Ca làm không thuộc chi nhánh.");
        if (!shift.IsEffectiveOn(request.WorkDate))
        {
            throw new BusinessRuleException("shift_not_effective", "Ca làm không áp dụng cho ngày đã chọn.");
        }

        var onLeave = await db.LeaveRequests.AnyAsync(l => l.EmployeeId == employee.Id && l.Status == LeaveStatus.Approved
            && l.StartDate <= request.WorkDate && l.EndDate >= request.WorkDate, ct);
        if (onLeave)
        {
            throw new BusinessRuleException("employee_on_leave", "Nhân viên có lịch nghỉ phép đã duyệt vào ngày này.");
        }

        // Conflict detection: any overlapping shift window for the same employee (incl. overnight shifts of adjacent days).
        var newWindow = shift.WindowOn(request.WorkDate);
        var nearby = await db.ShiftAssignments.Include(a => a.WorkShift)
            .Where(a => a.EmployeeId == employee.Id && a.WorkDate >= request.WorkDate.AddDays(-1) && a.WorkDate <= request.WorkDate.AddDays(1))
            .ToListAsync(ct);
        var conflict = nearby.FirstOrDefault(a => AttendanceCalculator.Overlaps(a.WorkShift!.WindowOn(a.WorkDate), newWindow));
        if (conflict is not null)
        {
            throw new BusinessRuleException("shift_conflict", $"Trùng lịch với ca '{conflict.WorkShift!.Name}' ngày {conflict.WorkDate:yyyy-MM-dd}.");
        }

        var assignment = new EmployeeShiftAssignment
        {
            BranchId = branchId,
            EmployeeId = employee.Id,
            WorkShiftId = shift.Id,
            WorkDate = request.WorkDate,
            Note = request.Note,
            CreatedByUserId = currentUser.RequireUserId(),
            CreatedAtUtc = clock.UtcNow,
        };
        db.ShiftAssignments.Add(assignment);
        audit.Record("shift.assigned", "EmployeeShiftAssignment", assignment.Id.ToString(), branchId,
            new { assignment.EmployeeId, assignment.WorkShiftId, assignment.WorkDate });
        await db.SaveChangesAsync(ct);
        return new ShiftAssignmentDto(assignment.Id, branchId, employee.Id, employee.FullName, shift.Id, shift.Name, assignment.WorkDate,
            shift.StartTime, shift.EndTime, assignment.Note);
    }

    public async Task RemoveAssignmentAsync(Guid branchId, Guid assignmentId, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        var a = await db.ShiftAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.BranchId == branchId, ct)
            ?? throw new NotFoundException("ShiftAssignment", assignmentId);
        if (await db.AttendanceRecords.AnyAsync(r => r.ShiftAssignmentId == a.Id, ct))
        {
            throw new BusinessRuleException("assignment_has_attendance", "Không thể xóa phân ca đã có chấm công.");
        }

        db.ShiftAssignments.Remove(a);
        audit.Record("shift.unassigned", "EmployeeShiftAssignment", a.Id.ToString(), branchId, new { a.EmployeeId, a.WorkShiftId, a.WorkDate });
        await db.SaveChangesAsync(ct);
    }

    private static void ValidateShift(UpsertWorkShiftRequest r) =>
        new Validator()
            .Required(r.Name, "name", 50)
            .Must(r.StartTime != r.EndTime, "endTime", "Giờ kết thúc phải khác giờ bắt đầu.")
            .Range(r.PayMultiplier, 0.1m, 10m, "payMultiplier")
            .Must(r.EffectiveTo is null || r.EffectiveTo >= r.EffectiveFrom, "effectiveTo", "Ngày kết thúc phải sau ngày bắt đầu.")
            .ThrowIfInvalid();

    private static WorkShiftDto ToDto(WorkShift s) => new(s.Id, s.BranchId, s.Name, s.StartTime, s.EndTime, s.PayMultiplier,
        s.EffectiveFrom, s.EffectiveTo, s.IsActive, s.CrossesMidnight);
}
