using Fnb.Domain.Common;

namespace Fnb.Domain.Hr;

public sealed class Employee : Entity, IBranchScoped, IHasTimestamps, IHasRowVersion
{
    public Guid BranchId { get; set; }
    /// <summary>Unique across the organization.</summary>
    public required string EmployeeCode { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Position { get; set; } = "Staff";
    /// <summary>Base hourly rate in VND used for basic payroll estimation.</summary>
    public decimal HourlyRate { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Optional login account linked to this employee (for self check-in, own schedule).</summary>
    public Guid? UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WorkShift : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal PayMultiplier { get; set; } = 1m;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    public bool CrossesMidnight => EndTime <= StartTime;

    public bool IsEffectiveOn(DateOnly date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    /// <summary>Local (branch time) window of this shift on a given work date.</summary>
    public (DateTime Start, DateTime End) WindowOn(DateOnly workDate)
    {
        var start = workDate.ToDateTime(StartTime);
        var end = (CrossesMidnight ? workDate.AddDays(1) : workDate).ToDateTime(EndTime);
        return (start, end);
    }
}

public sealed class EmployeeShiftAssignment : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid WorkShiftId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Employee? Employee { get; set; }
    public WorkShift? WorkShift { get; set; }
}

public enum LeaveStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

public sealed class LeaveRequest : Entity, IBranchScoped, IHasRowVersion
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public required string Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Employee? Employee { get; set; }

    public void Review(bool approve, Guid reviewerId, string? note, DateTime nowUtc)
    {
        if (Status != LeaveStatus.Pending)
        {
            throw new BusinessRuleException("leave_not_pending", "Only pending leave requests can be reviewed.");
        }

        Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
        ReviewedByUserId = reviewerId;
        ReviewedAtUtc = nowUtc;
        ReviewNote = note;
    }
}

public enum AttendanceSource
{
    Web = 1,
    Kiosk = 2,
    Manager = 3,
}

public sealed class AttendanceRecord : Entity, IBranchScoped, IHasRowVersion
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public AttendanceSource CheckInSource { get; set; }
    public AttendanceSource? CheckOutSource { get; set; }
    public string? CheckInIp { get; set; }
    public string? CheckOutIp { get; set; }
    public Guid? ShiftAssignmentId { get; set; }
    /// <summary>Business date (branch local) of the check-in; an overnight shift belongs to the date it started.</summary>
    public DateOnly WorkDate { get; set; }
    public int? WorkedMinutes { get; set; }
    public int? PayableMinutes { get; set; }
    /// <summary>Rounding rule applied when computing PayableMinutes, kept for traceability.</summary>
    public string? RoundingRule { get; set; }
    public decimal PayMultiplier { get; set; } = 1m;
    public bool IsLocked { get; set; }
    public Guid? PayrollPeriodId { get; set; }
    public Guid PerformedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Employee? Employee { get; set; }

    public void CheckOut(DateTime nowUtc, AttendanceSource source, string? ip, AttendanceRoundingPolicy policy)
    {
        if (IsLocked)
        {
            throw new BusinessRuleException("attendance_locked", "Attendance record belongs to a closed payroll period.");
        }

        if (CheckOutUtc is not null)
        {
            throw new BusinessRuleException("already_checked_out", "This attendance record is already checked out.");
        }

        if (nowUtc < CheckInUtc)
        {
            throw new BusinessRuleException("invalid_checkout_time", "Check-out time cannot be earlier than check-in time.");
        }

        CheckOutUtc = nowUtc;
        CheckOutSource = source;
        CheckOutIp = ip;
        WorkedMinutes = AttendanceCalculator.WorkedMinutes(CheckInUtc, nowUtc);
        PayableMinutes = policy.Apply(WorkedMinutes.Value);
        RoundingRule = policy.Describe();
    }
}

/// <summary>Snapshot of a closed payroll period. Inputs/results are persisted for traceability.</summary>
public sealed class PayrollPeriod : Entity, IBranchScoped
{
    public Guid BranchId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid ClosedByUserId { get; set; }
    public DateTime ClosedAtUtc { get; set; }
    public required string Formula { get; set; }
    public decimal TotalAmount { get; set; }
    public List<PayrollLine> Lines { get; set; } = [];
}

public sealed class PayrollLine : Entity
{
    public Guid PayrollPeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string EmployeeName { get; set; }
    public int ShiftCount { get; set; }
    public int WorkedMinutes { get; set; }
    public int PayableMinutes { get; set; }
    /// <summary>Sum of payable hours × shift multiplier.</summary>
    public decimal WeightedHours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal Amount { get; set; }
}
