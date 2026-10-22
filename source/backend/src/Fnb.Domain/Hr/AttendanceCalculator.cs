namespace Fnb.Domain.Hr;

public enum RoundingMode
{
    /// <summary>Round down to the increment (conservative; default).</summary>
    Floor = 1,
    /// <summary>Round to the nearest increment (midpoint rounds up).</summary>
    Nearest = 2,
    /// <summary>Round up to the increment.</summary>
    Ceiling = 3,
}

/// <summary>Configurable rounding of worked minutes into payable minutes.</summary>
public sealed record AttendanceRoundingPolicy(int IncrementMinutes, RoundingMode Mode)
{
    public static AttendanceRoundingPolicy Default { get; } = new(15, RoundingMode.Floor);

    public int Apply(int workedMinutes)
    {
        if (workedMinutes <= 0)
        {
            return 0;
        }

        if (IncrementMinutes <= 1)
        {
            return workedMinutes;
        }

        var units = (decimal)workedMinutes / IncrementMinutes;
        var rounded = Mode switch
        {
            RoundingMode.Floor => Math.Floor(units),
            RoundingMode.Ceiling => Math.Ceiling(units),
            _ => Math.Round(units, MidpointRounding.AwayFromZero),
        };
        return (int)rounded * IncrementMinutes;
    }

    public string Describe() => $"{Mode}:{IncrementMinutes}m";
}

public static class AttendanceCalculator
{
    /// <summary>Whole minutes between check-in and check-out; works across midnight because both are UTC instants.</summary>
    public static int WorkedMinutes(DateTime checkInUtc, DateTime checkOutUtc)
    {
        if (checkOutUtc <= checkInUtc)
        {
            return 0;
        }

        return (int)Math.Floor((checkOutUtc - checkInUtc).TotalMinutes);
    }

    /// <summary>
    /// Basic pay formula (documented in docs/assumptions-and-decisions.md):
    /// amount = round( Σ(payableMinutes/60 × shiftMultiplier) × hourlyRate, 0 VND, away-from-zero ).
    /// Not a statutory payroll calculation.
    /// </summary>
    public static decimal BasicPay(decimal weightedHours, decimal hourlyRate) =>
        Math.Round(weightedHours * hourlyRate, 0, MidpointRounding.AwayFromZero);

    public static decimal WeightedHours(int payableMinutes, decimal multiplier) =>
        Math.Round(payableMinutes / 60m * multiplier, 4, MidpointRounding.AwayFromZero);

    /// <summary>True when the two local time windows overlap (end is exclusive).</summary>
    public static bool Overlaps((DateTime Start, DateTime End) a, (DateTime Start, DateTime End) b) =>
        a.Start < b.End && b.Start < a.End;
}
