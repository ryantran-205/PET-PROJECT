namespace Fnb.Application.Common;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "fnb-api";
    public string Audience { get; set; } = "fnb-clients";
    /// <summary>HMAC signing key (≥ 32 bytes). Must come from environment/user-secrets/Key Vault.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class SecurityOptions
{
    public const string Section = "Security";
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int MinPasswordLength { get; set; } = 8;
}

public sealed class AttendanceOptions
{
    public const string Section = "Attendance";
    public int RoundingIncrementMinutes { get; set; } = 15;
    public string RoundingMode { get; set; } = "Floor";
    /// <summary>Maximum open duration before a check-in is considered abandoned and must be closed by a manager.</summary>
    public int MaxShiftHours { get; set; } = 16;
}

public sealed class OrderingOptions
{
    public const string Section = "Ordering";
    public int MaxLinesPerOrder { get; set; } = 30;
    public int MaxQuantityPerLine { get; set; } = 50;
    public int MaxNoteLength { get; set; } = 200;
    public int MaxAddOnsPerLine { get; set; } = 10;
    public bool AllowCustomerCancel { get; set; } = true;
    public int IdempotencyHours { get; set; } = 24;
    /// <summary>Base URL of the customer web app, used for tracking links/QR.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}

public sealed class ReportOptions
{
    public const string Section = "Reports";
    public int MaxRangeDays { get; set; } = 366;
    public int MaxExportRows { get; set; } = 100_000;
}

public sealed class CacheOptions
{
    public const string Section = "Cache";
    public int PublicMenuSeconds { get; set; } = 300;
}
