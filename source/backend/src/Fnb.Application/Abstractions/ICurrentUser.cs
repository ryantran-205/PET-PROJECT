using Fnb.Domain.Common;
using Fnb.Domain.Identity;

namespace Fnb.Application.Abstractions;

/// <summary>The authenticated principal executing the current use case (or the system for background work).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string UserName { get; }
    UserRole? Role { get; }
    IReadOnlyCollection<Guid> BranchIds { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
    bool IsSystem { get; }
}

public static class CurrentUserExtensions
{
    public static bool IsSuperAdmin(this ICurrentUser user) => user.IsSystem || user.Role == UserRole.SuperAdmin;

    public static bool IsBranchAdminOrAbove(this ICurrentUser user) =>
        user.IsSystem || user.Role is UserRole.SuperAdmin or UserRole.BranchAdmin;

    public static bool CanAccessBranch(this ICurrentUser user, Guid branchId) =>
        user.IsSuperAdmin() || user.BranchIds.Contains(branchId);

    /// <summary>Deny-by-default branch scope check. Throws 403 when the branch is not assigned to the user.</summary>
    public static void EnsureBranchAccess(this ICurrentUser user, Guid branchId)
    {
        if (!user.IsAuthenticated && !user.IsSystem)
        {
            throw new UnauthorizedException();
        }

        if (!user.CanAccessBranch(branchId))
        {
            throw new ForbiddenException("You do not have access to this branch.");
        }
    }

    public static void EnsureBranchAdmin(this ICurrentUser user, Guid branchId)
    {
        user.EnsureBranchAccess(branchId);
        if (!user.IsBranchAdminOrAbove())
        {
            throw new ForbiddenException("Branch administrator permission is required.");
        }
    }

    public static void EnsureSuperAdmin(this ICurrentUser user)
    {
        if (!user.IsSuperAdmin())
        {
            throw new ForbiddenException("Super administrator permission is required.");
        }
    }

    public static Guid RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new UnauthorizedException();

    /// <summary>Restricts a requested branch filter to what the user may see. Null = all permitted branches.</summary>
    public static IReadOnlyCollection<Guid>? ResolveBranchFilter(this ICurrentUser user, Guid? requestedBranchId)
    {
        if (requestedBranchId is { } id)
        {
            user.EnsureBranchAccess(id);
            return [id];
        }

        return user.IsSuperAdmin() ? null : user.BranchIds;
    }
}

public sealed class SystemUser : ICurrentUser
{
    public static readonly SystemUser Instance = new();
    public bool IsAuthenticated => true;
    public Guid? UserId => null;
    public string UserName => "system";
    public UserRole? Role => null;
    public IReadOnlyCollection<Guid> BranchIds => [];
    public string? IpAddress => null;
    public string? CorrelationId { get; init; }
    public bool IsSystem => true;
}
