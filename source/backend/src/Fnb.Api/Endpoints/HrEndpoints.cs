using Fnb.Api.Security;
using Fnb.Application.Hr;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Fnb.Api.Endpoints;

public static class HrEndpoints
{
    public static IEndpointRouteBuilder MapHrEndpoints(this IEndpointRouteBuilder app)
    {
        var branch = app.MapGroup("/branches/{branchId:guid}").WithTags("HR").RequireAuthorization(Policies.Staff);
        branch.MapGet("/employees", (Guid branchId, [AsParameters] EmployeeQuery query, EmployeeService svc, CancellationToken ct) => svc.ListAsync(branchId, query, ct));
        branch.MapPost("/employees", async Task<Created<EmployeeDto>> (Guid branchId, CreateEmployeeRequest request, EmployeeService svc, CancellationToken ct) =>
        {
            var e = await svc.CreateAsync(branchId, request, ct);
            return TypedResults.Created($"/api/v1/employees/{e.Id}", e);
        }).RequireAuthorization(Policies.BranchAdmin);

        branch.MapGet("/shifts", (Guid branchId, ShiftService svc, CancellationToken ct) => svc.ListShiftsAsync(branchId, ct));
        branch.MapPost("/shifts", async Task<Created<WorkShiftDto>> (Guid branchId, UpsertWorkShiftRequest request, ShiftService svc, CancellationToken ct) =>
        {
            var s = await svc.CreateShiftAsync(branchId, request, ct);
            return TypedResults.Created($"/api/v1/branches/{branchId}/shifts/{s.Id}", s);
        }).RequireAuthorization(Policies.BranchAdmin);
        branch.MapPut("/shifts/{shiftId:guid}", (Guid branchId, Guid shiftId, UpsertWorkShiftRequest request, ShiftService svc, CancellationToken ct) =>
            svc.UpdateShiftAsync(branchId, shiftId, request, ct)).RequireAuthorization(Policies.BranchAdmin);

        branch.MapGet("/shift-assignments", (Guid branchId, [AsParameters] ShiftAssignmentQuery query, ShiftService svc, CancellationToken ct) =>
            svc.ListAssignmentsAsync(branchId, query, ct));
        branch.MapPost("/shift-assignments", async Task<Created<ShiftAssignmentDto>> (Guid branchId, CreateShiftAssignmentRequest request, ShiftService svc, CancellationToken ct) =>
        {
            var a = await svc.AssignAsync(branchId, request, ct);
            return TypedResults.Created($"/api/v1/branches/{branchId}/shift-assignments/{a.Id}", a);
        }).RequireAuthorization(Policies.BranchAdmin);
        branch.MapDelete("/shift-assignments/{assignmentId:guid}", async (Guid branchId, Guid assignmentId, ShiftService svc, CancellationToken ct) =>
        {
            await svc.RemoveAssignmentAsync(branchId, assignmentId, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.BranchAdmin);

        branch.MapGet("/payroll-periods", (Guid branchId, PayrollService svc, CancellationToken ct) => svc.ListPeriodsAsync(branchId, ct))
            .RequireAuthorization(Policies.BranchAdmin);
        branch.MapPost("/payroll-periods", (Guid branchId, ClosePeriodBody body, PayrollService svc, CancellationToken ct) =>
                svc.CloseAsync(new ClosePayrollRequest(branchId, body.From, body.To), ct))
            .RequireAuthorization(Policies.BranchAdmin).WithSummary("Chốt kỳ công: khóa chấm công và lưu snapshot bảng lương cơ bản.");

        var employees = app.MapGroup("/employees").WithTags("HR").RequireAuthorization(Policies.Staff);
        employees.MapGet("/{employeeId:guid}", (Guid employeeId, EmployeeService svc, CancellationToken ct) => svc.GetAsync(employeeId, ct));
        employees.MapPut("/{employeeId:guid}", (Guid employeeId, UpdateEmployeeRequest request, EmployeeService svc, CancellationToken ct) =>
            svc.UpdateAsync(employeeId, request, ct)).RequireAuthorization(Policies.BranchAdmin);

        var attendance = app.MapGroup("/attendance").WithTags("HR").RequireAuthorization(Policies.Staff);
        attendance.MapPost("/check-in", (CheckInRequest request, AttendanceService svc, CancellationToken ct) => svc.CheckInAsync(request, ct));
        attendance.MapPost("/check-out", (CheckOutRequest request, AttendanceService svc, CancellationToken ct) => svc.CheckOutAsync(request, ct));
        attendance.MapGet("/", ([AsParameters] AttendanceQuery query, AttendanceService svc, CancellationToken ct) => svc.ListAsync(query, ct));
        attendance.MapGet("/current", async (AttendanceService svc, CancellationToken ct) =>
        {
            var current = await svc.CurrentAsync(ct);
            return current is null ? Results.NoContent() : Results.Ok(current);
        });
        attendance.MapPut("/{attendanceId:guid}", (Guid attendanceId, AdjustAttendanceRequest request, AttendanceService svc, CancellationToken ct) =>
            svc.AdjustAsync(attendanceId, request, ct)).RequireAuthorization(Policies.BranchAdmin).WithSummary("Điều chỉnh chấm công (bắt buộc lý do, có audit).");

        var leave = app.MapGroup("/leave-requests").WithTags("HR").RequireAuthorization(Policies.Staff);
        leave.MapGet("/", ([AsParameters] LeaveQuery query, LeaveService svc, CancellationToken ct) => svc.ListAsync(query, ct));
        leave.MapPost("/", async Task<Created<LeaveRequestDto>> (CreateLeaveRequest request, LeaveService svc, CancellationToken ct) =>
        {
            var l = await svc.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/leave-requests/{l.Id}", l);
        });
        leave.MapPost("/{id:guid}/approve", (Guid id, ReviewLeaveRequest request, LeaveService svc, CancellationToken ct) => svc.ApproveAsync(id, request, ct))
            .RequireAuthorization(Policies.BranchAdmin);
        leave.MapPost("/{id:guid}/reject", (Guid id, ReviewLeaveRequest request, LeaveService svc, CancellationToken ct) => svc.RejectAsync(id, request, ct))
            .RequireAuthorization(Policies.BranchAdmin);

        app.MapGet("/reports/payroll-summary", (Guid branchId, DateOnly from, DateOnly to, PayrollService svc, CancellationToken ct) =>
                svc.SummaryAsync(branchId, from, to, ct))
            .WithTags("Reports").RequireAuthorization(Policies.BranchAdmin);
        return app;
    }

    public sealed record ClosePeriodBody(DateOnly From, DateOnly To);
}
