using Fnb.Domain.Common;

namespace Fnb.Domain.Identity;

public enum UserRole
{
    SuperAdmin = 1,
    BranchAdmin = 2,
    BranchStaff = 3,
}

public sealed class User : Entity, IHasTimestamps, IHasRowVersion
{
    public required string UserName { get; set; }
    public required string NormalizedUserName { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    /// <summary>Changes whenever credentials/roles/assignments change; embedded in tokens to invalidate them.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<UserBranchAssignment> BranchAssignments { get; set; } = [];

    public static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");
}

public sealed class UserBranchAssignment
{
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    /// <summary>SHA-256 hash of the opaque token; the raw token is never stored.</summary>
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
