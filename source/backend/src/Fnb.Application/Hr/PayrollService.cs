using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Application.Hr;

public sealed record PayrollSummaryLineDto(Guid EmployeeId, string EmployeeCode, string EmployeeName, int ShiftCount, int WorkedMinutes,
    int PayableMinutes, decimal WeightedHours, decimal HourlyRate, decimal Amount);

public sealed record PayrollSummaryDto(Guid BranchId, DateOnly From, DateOnly To, string Formula, IReadOnlyList<PayrollSummaryLineDto> Lines,
    decimal TotalAmount, int OpenRecords, Guid? ClosedPeriodId);

public sealed record ClosePayrollRequest(Guid BranchId, DateOnly From, DateOnly To);

public sealed record PayrollPeriodDto(Guid Id, Guid BranchId, DateOnly PeriodStart, DateOnly PeriodEnd, DateTime ClosedAtUtc, decimal TotalAmount, int LineCount);

/// <summary>
/// Basic payroll estimation (NOT statutory payroll, tax or insurance).
/// Formula: amount = round(Σ(payableMinutes/60 × shiftMultiplier) × hourlyRate, 0).
/// </summary>
public sealed class PayrollService(IAppDbContext db, IClock clock, IAuditService audit, ICurrentUser currentUser)
{
    public const string Formula = "amount = round(sum(payableMinutes/60 * shiftMultiplier) * hourlyRate, 0, away-from-zero)";

    public async Task<PayrollSummaryDto> SummaryAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        ValidateRange(from, to);
        var (lines, open) = await ComputeAsync(branchId, from, to, ct);
        var closed = await db.PayrollPeriods.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.PeriodStart == from && p.PeriodEnd == to)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return new PayrollSummaryDto(branchId, from, to, Formula, lines, lines.Sum(l => l.Amount), open, closed);
    }

    public async Task<PayrollPeriodDto> CloseAsync(ClosePayrollRequest request, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(request.BranchId);
        ValidateRange(request.From, request.To);

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var overlapping = await db.PayrollPeriods.AnyAsync(p => p.BranchId == request.BranchId
            && p.PeriodStart <= request.To && p.PeriodEnd >= request.From, ct);
        if (overlapping)
        {
            throw new BusinessRuleException("payroll_period_overlap", "Đã có kỳ lương được chốt trùng khoảng thời gian.");
        }

        var (lines, open) = await ComputeAsync(request.BranchId, request.From, request.To, ct);
        if (open > 0)
        {
            throw new BusinessRuleException("open_attendance", $"Còn {open} bản ghi chấm công chưa check-out trong kỳ.");
        }

        var period = new PayrollPeriod
        {
            BranchId = request.BranchId,
            PeriodStart = request.From,
            PeriodEnd = request.To,
            ClosedByUserId = currentUser.RequireUserId(),
            ClosedAtUtc = clock.UtcNow,
            Formula = Formula,
            TotalAmount = lines.Sum(l => l.Amount),
            Lines = lines.Select(l => new PayrollLine
            {
                EmployeeId = l.EmployeeId,
                EmployeeCode = l.EmployeeCode,
                EmployeeName = l.EmployeeName,
                ShiftCount = l.ShiftCount,
                WorkedMinutes = l.WorkedMinutes,
                PayableMinutes = l.PayableMinutes,
                WeightedHours = l.WeightedHours,
                HourlyRate = l.HourlyRate,
                Amount = l.Amount,
            }).ToList(),
        };
        db.PayrollPeriods.Add(period);
        audit.Record("payroll.closed", "PayrollPeriod", period.Id.ToString(), request.BranchId,
            new { request.From, request.To, period.TotalAmount, lines = lines.Count });
        await db.SaveChangesAsync(ct);

        await db.AttendanceRecords
            .Where(r => r.BranchId == request.BranchId && r.WorkDate >= request.From && r.WorkDate <= request.To && !r.IsLocked)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsLocked, true).SetProperty(r => r.PayrollPeriodId, period.Id), ct);
        await tx.CommitAsync(ct);
        return new PayrollPeriodDto(period.Id, period.BranchId, period.PeriodStart, period.PeriodEnd, period.ClosedAtUtc, period.TotalAmount, period.Lines.Count);
    }

    public async Task<IReadOnlyList<PayrollPeriodDto>> ListPeriodsAsync(Guid branchId, CancellationToken ct)
    {
        currentUser.EnsureBranchAdmin(branchId);
        return await db.PayrollPeriods.AsNoTracking().Where(p => p.BranchId == branchId).OrderByDescending(p => p.PeriodStart)
            .Select(p => new PayrollPeriodDto(p.Id, p.BranchId, p.PeriodStart, p.PeriodEnd, p.ClosedAtUtc, p.TotalAmount, p.Lines.Count))
            .ToListAsync(ct);
    }

    private async Task<(List<PayrollSummaryLineDto> Lines, int Open)> ComputeAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var records = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.WorkDate >= from && r.WorkDate <= to)
            .Select(r => new { r.EmployeeId, r.WorkedMinutes, r.PayableMinutes, r.PayMultiplier, r.CheckOutUtc })
            .ToListAsync(ct);
        var open = records.Count(r => r.CheckOutUtc == null);
        var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

        var lines = records.Where(r => r.CheckOutUtc != null)
            .GroupBy(r => r.EmployeeId)
            .Select(g =>
            {
                var e = employees[g.Key];
                var weighted = g.Sum(r => AttendanceCalculator.WeightedHours(r.PayableMinutes ?? 0, r.PayMultiplier));
                return new PayrollSummaryLineDto(e.Id, e.EmployeeCode, e.FullName, g.Count(), g.Sum(r => r.WorkedMinutes ?? 0),
                    g.Sum(r => r.PayableMinutes ?? 0), weighted, e.HourlyRate, AttendanceCalculator.BasicPay(weighted, e.HourlyRate));
            })
            .OrderBy(l => l.EmployeeCode)
            .ToList();
        return (lines, open);
    }

    private static void ValidateRange(DateOnly from, DateOnly to) =>
        new Validator()
            .Must(to >= from, "to", "Ngày kết thúc phải sau ngày bắt đầu.")
            .Must(to.DayNumber - from.DayNumber <= 62, "to", "Kỳ lương tối đa 62 ngày.")
            .ThrowIfInvalid();
}
