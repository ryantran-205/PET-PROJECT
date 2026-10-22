using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Hr;

public sealed record LeaveRequestDto(Guid Id, Guid BranchId, Guid EmployeeId, string EmployeeName, DateOnly StartDate, DateOnly EndDate,
    string Reason, string Status, Guid? ReviewedByUserId, DateTime? ReviewedAtUtc, string? ReviewNote, DateTime CreatedAtUtc);

public sealed record CreateLeaveRequest(Guid? EmployeeId, DateOnly StartDate, DateOnly EndDate, string Reason);

public sealed record ReviewLeaveRequest(string? Note);

public sealed record LeaveQuery(Guid? BranchId, string? Status, int Page = 1, int PageSize = 50);

public sealed class LeaveService(IAppDbContext db, IClock clock, IAuditService audit, ICurrentUser currentUser)
{
    public async Task<PagedResult<LeaveRequestDto>> ListAsync(LeaveQuery query, CancellationToken ct)
    {
        var q = db.LeaveRequests.AsNoTracking().AsQueryable();
        if (currentUser.IsBranchAdminOrAbove())
        {
            var branches = currentUser.ResolveBranchFilter(query.BranchId);
            if (branches is not null)
            {
                q = q.Where(l => branches.Contains(l.BranchId));
            }
        }
        else
        {
            var uid = currentUser.RequireUserId();
            q = q.Where(l => l.Employee!.UserId == uid);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<LeaveStatus>(query.Status, true, out var status))
        {
            q = q.Where(l => l.Status == status);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(l => l.CreatedAtUtc).Skip(page.Skip).Take(page.SafePageSize)
            .Select(l => new LeaveRequestDto(l.Id, l.BranchId, l.EmployeeId, l.Employee!.FullName, l.StartDate, l.EndDate, l.Reason,
                l.Status.ToString(), l.ReviewedByUserId, l.ReviewedAtUtc, l.ReviewNote, l.CreatedAtUtc))
            .ToListAsync(ct);
        return new PagedResult<LeaveRequestDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<LeaveRequestDto> CreateAsync(CreateLeaveRequest request, CancellationToken ct)
    {
        var uid = currentUser.RequireUserId();
        new Validator()
            .Required(request.Reason, "reason", 500)
            .Must(request.EndDate >= request.StartDate, "endDate", "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.")
            .Must(request.EndDate.DayNumber - request.StartDate.DayNumber <= 60, "endDate", "Tối đa 60 ngày cho một yêu cầu.")
            .ThrowIfInvalid();

        Employee employee;
        if (request.EmployeeId is { } eid)
        {
            employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == eid, ct) ?? throw new NotFoundException("Employee", eid);
            if (employee.UserId != uid)
            {
                currentUser.EnsureBranchAdmin(employee.BranchId);
            }
        }
        else
        {
            employee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == uid, ct)
                ?? throw new BusinessRuleException("no_employee_profile", "Tài khoản chưa được liên kết hồ sơ nhân viên.");
        }

        currentUser.EnsureBranchAccess(employee.BranchId);
        var overlap = await db.LeaveRequests.AnyAsync(l => l.EmployeeId == employee.Id
            && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
            && l.StartDate <= request.EndDate && l.EndDate >= request.StartDate, ct);
        if (overlap)
        {
            throw new BusinessRuleException("leave_overlap", "Đã có yêu cầu nghỉ phép trùng khoảng thời gian.");
        }

        var leave = new LeaveRequest
        {
            BranchId = employee.BranchId,
            EmployeeId = employee.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason.Trim(),
            RequestedByUserId = uid,
            CreatedAtUtc = clock.UtcNow,
        };
        db.LeaveRequests.Add(leave);
        audit.Record("leave.requested", "LeaveRequest", leave.Id.ToString(), leave.BranchId, new { leave.EmployeeId, leave.StartDate, leave.EndDate });
        await db.SaveChangesAsync(ct);
        return ToDto(leave, employee.FullName);
    }

    public Task<LeaveRequestDto> ApproveAsync(Guid id, ReviewLeaveRequest request, CancellationToken ct) => ReviewAsync(id, true, request, ct);

    public Task<LeaveRequestDto> RejectAsync(Guid id, ReviewLeaveRequest request, CancellationToken ct) => ReviewAsync(id, false, request, ct);

    private async Task<LeaveRequestDto> ReviewAsync(Guid id, bool approve, ReviewLeaveRequest request, CancellationToken ct)
    {
        new Validator().MaxLength(request.Note, "note", 500)
            .Must(approve || !string.IsNullOrWhiteSpace(request.Note), "note", "Cần ghi lý do từ chối.")
            .ThrowIfInvalid();
        var leave = await db.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("LeaveRequest", id);
        currentUser.EnsureBranchAdmin(leave.BranchId);
        if (leave.Employee!.UserId == currentUser.UserId && !currentUser.IsSuperAdmin())
        {
            throw new ForbiddenException("Không thể tự duyệt yêu cầu nghỉ phép của chính mình.");
        }

        leave.Review(approve, currentUser.RequireUserId(), request.Note, clock.UtcNow);
        audit.Record(approve ? "leave.approved" : "leave.rejected", "LeaveRequest", leave.Id.ToString(), leave.BranchId, new { request.Note });
        await db.SaveChangesAsync(ct);
        return ToDto(leave, leave.Employee.FullName);
    }

    private static LeaveRequestDto ToDto(LeaveRequest l, string name) => new(l.Id, l.BranchId, l.EmployeeId, name, l.StartDate, l.EndDate,
        l.Reason, l.Status.ToString(), l.ReviewedByUserId, l.ReviewedAtUtc, l.ReviewNote, l.CreatedAtUtc);
}
