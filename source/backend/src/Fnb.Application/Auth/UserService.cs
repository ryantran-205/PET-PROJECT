using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fnb.Application.Auth;

public sealed record UserDto(Guid Id, string UserName, string DisplayName, string? Email, string Role, bool IsActive, IReadOnlyList<Guid> BranchIds, DateTime? LastLoginAtUtc, DateTime CreatedAtUtc);

public sealed record CreateUserRequest(string UserName, string DisplayName, string? Email, string Password, string Role, IReadOnlyList<Guid>? BranchIds);

public sealed record UpdateUserRequest(string DisplayName, string? Email, string Role, bool IsActive, IReadOnlyList<Guid>? BranchIds);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record UserQuery(Guid? BranchId, string? Search, int Page = 1, int PageSize = 20);

/// <summary>
/// Account administration. SuperAdmin manages every account. BranchAdmin may only manage BranchStaff accounts
/// restricted to branches assigned to the BranchAdmin.
/// </summary>
public sealed class UserService(
    IAppDbContext db,
    IPasswordService passwords,
    IClock clock,
    IAuditService audit,
    ICurrentUser currentUser,
    IOptions<SecurityOptions> securityOptions)
{
    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct)
    {
        if (!currentUser.IsBranchAdminOrAbove())
        {
            throw new ForbiddenException();
        }

        var branchFilter = currentUser.ResolveBranchFilter(query.BranchId);
        var q = db.Users.AsNoTracking().Include(u => u.BranchAssignments).AsQueryable();
        if (branchFilter is not null)
        {
            q = q.Where(u => u.BranchAssignments.Any(a => branchFilter.Contains(a.BranchId)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(u => u.UserName.Contains(s) || u.DisplayName.Contains(s));
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(u => u.UserName).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<UserDto>(items.Select(ToDto).ToList(), page.SafePage, page.SafePageSize, total);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var role = ParseRole(request.Role);
        var branchIds = (request.BranchIds ?? []).Distinct().ToList();
        var v = new Validator()
            .Required(request.UserName, "userName", 100)
            .Required(request.DisplayName, "displayName", 150)
            .MaxLength(request.Email, "email", 200)
            .Required(request.Password, "password", 200)
            .Must(request.UserName is null || request.UserName.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'), "userName", "Chỉ dùng chữ, số, dấu chấm, gạch dưới, gạch ngang.")
            .Must(role == UserRole.SuperAdmin || branchIds.Count > 0, "branchIds", "Tài khoản chi nhánh phải được gán ít nhất một chi nhánh.");
        v.ThrowIfInvalid();
        PasswordPolicy.Validate(request.Password, securityOptions.Value.MinPasswordLength, "password");
        await EnsureCanManageAsync(role, branchIds, ct);

        var normalized = User.Normalize(request.UserName!);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, ct))
        {
            throw new ConflictException("username_taken", "Tên đăng nhập đã tồn tại.");
        }

        var user = new User
        {
            UserName = request.UserName!.Trim(),
            NormalizedUserName = normalized,
            DisplayName = request.DisplayName!.Trim(),
            Email = request.Email?.Trim(),
            Role = role,
            IsActive = true,
            CreatedAtUtc = clock.UtcNow,
        };
        user.PasswordHash = passwords.Hash(user, request.Password);
        user.BranchAssignments = role == UserRole.SuperAdmin ? [] : branchIds.Select(b => new UserBranchAssignment { UserId = user.Id, BranchId = b }).ToList();
        db.Users.Add(user);
        audit.Record("user.created", "User", user.Id.ToString(), branchIds.Count == 1 ? branchIds[0] : null,
            new { user.UserName, role = role.ToString(), branchIds });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var role = ParseRole(request.Role);
        var branchIds = (request.BranchIds ?? []).Distinct().ToList();
        new Validator()
            .Required(request.DisplayName, "displayName", 150)
            .MaxLength(request.Email, "email", 200)
            .Must(role == UserRole.SuperAdmin || branchIds.Count > 0, "branchIds", "Tài khoản chi nhánh phải được gán ít nhất một chi nhánh.")
            .ThrowIfInvalid();

        var user = await db.Users.Include(u => u.BranchAssignments).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User", id);

        // Must be allowed to manage both the current and the target state.
        await EnsureCanManageAsync(user.Role, user.BranchAssignments.Select(a => a.BranchId).ToList(), ct);
        await EnsureCanManageAsync(role, branchIds, ct);

        if (user.Id == currentUser.UserId && (!request.IsActive || role != user.Role))
        {
            throw new BusinessRuleException("cannot_modify_self", "Không thể tự vô hiệu hóa hoặc đổi vai trò của chính mình.");
        }

        var before = new { user.DisplayName, role = user.Role.ToString(), user.IsActive, branchIds = user.BranchAssignments.Select(a => a.BranchId).ToList() };
        var securityChanged = user.Role != role || user.IsActive != request.IsActive
            || !user.BranchAssignments.Select(a => a.BranchId).OrderBy(x => x).SequenceEqual(branchIds.OrderBy(x => x));

        user.DisplayName = request.DisplayName.Trim();
        user.Email = request.Email?.Trim();
        user.Role = role;
        user.IsActive = request.IsActive;
        user.UpdatedAtUtc = clock.UtcNow;
        List<Guid> targetBranches = role == UserRole.SuperAdmin ? [] : branchIds;
        var currentAssignments = user.BranchAssignments.ToList();
        db.UserBranchAssignments.RemoveRange(currentAssignments.Where(a => !targetBranches.Contains(a.BranchId)));
        foreach (var b in targetBranches.Where(b => currentAssignments.All(a => a.BranchId != b)).ToList())
        {
            db.UserBranchAssignments.Add(new UserBranchAssignment { UserId = user.Id, BranchId = b });
        }

        if (securityChanged)
        {
            user.RotateSecurityStamp();
            await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, clock.UtcNow), ct);
        }

        audit.Record("user.updated", "User", user.Id.ToString(), null, new { before, after = new { user.DisplayName, role = role.ToString(), user.IsActive, branchIds } });
        await db.SaveChangesAsync(ct);
        user.BranchAssignments = branchIds.Select(b => new UserBranchAssignment { UserId = user.Id, BranchId = b }).ToList();
        return ToDto(user);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        new Validator().Required(request.NewPassword, "newPassword").ThrowIfInvalid();
        PasswordPolicy.Validate(request.NewPassword, securityOptions.Value.MinPasswordLength, "newPassword");
        var user = await db.Users.Include(u => u.BranchAssignments).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User", id);
        await EnsureCanManageAsync(user.Role, user.BranchAssignments.Select(a => a.BranchId).ToList(), ct);

        user.PasswordHash = passwords.Hash(user, request.NewPassword);
        user.RotateSecurityStamp();
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = clock.UtcNow;
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, clock.UtcNow), ct);
        audit.Record("user.password_reset", "User", user.Id.ToString(), null);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureCanManageAsync(UserRole targetRole, IReadOnlyCollection<Guid> targetBranches, CancellationToken ct)
    {
        if (targetBranches.Count > 0)
        {
            var existing = await db.Branches.CountAsync(b => targetBranches.Contains(b.Id), ct);
            if (existing != targetBranches.Count)
            {
                throw ValidationFailedException.For("branchIds", "Chi nhánh không tồn tại.");
            }
        }

        if (currentUser.IsSuperAdmin())
        {
            return;
        }

        if (currentUser.Role != UserRole.BranchAdmin || targetRole != UserRole.BranchStaff)
        {
            throw new ForbiddenException("Chỉ Super Admin được quản lý tài khoản quản trị.");
        }

        if (targetBranches.Any(b => !currentUser.BranchIds.Contains(b)))
        {
            throw new ForbiddenException("Không thể gán chi nhánh ngoài phạm vi quản lý.");
        }
    }

    private static UserRole ParseRole(string? role) =>
        Enum.TryParse<UserRole>(role, true, out var r) && Enum.IsDefined(r)
            ? r
            : throw ValidationFailedException.For("role", "Vai trò không hợp lệ.");

    private static UserDto ToDto(User u) => new(u.Id, u.UserName, u.DisplayName, u.Email, u.Role.ToString(), u.IsActive,
        u.BranchAssignments.Select(a => a.BranchId).ToList(), u.LastLoginAtUtc, u.CreatedAtUtc);
}
