using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Common;
using Fnb.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fnb.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, MeResponse User);

public sealed record BranchSummary(Guid Id, string Code, string Name);

public sealed record MeResponse(Guid Id, string UserName, string DisplayName, string Role, IReadOnlyList<BranchSummary> Branches, Guid? EmployeeId);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed class AuthService(
    IAppDbContext db,
    IPasswordService passwords,
    ITokenService tokens,
    IClock clock,
    IAuditService audit,
    ICurrentUser currentUser,
    IOptions<JwtOptions> jwtOptions,
    IOptions<SecurityOptions> securityOptions)
{
    private const string InvalidCredentials = "Tên đăng nhập hoặc mật khẩu không đúng.";

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        new Validator()
            .Required(request.UserName, "userName", 100)
            .Required(request.Password, "password", 200)
            .ThrowIfInvalid();

        var normalized = User.Normalize(request.UserName);
        var user = await db.Users.Include(u => u.BranchAssignments)
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        var now = clock.UtcNow;

        if (user is null)
        {
            // Equalize timing to avoid user enumeration.
            passwords.VerifyDummy(request.Password);
            audit.Record("auth.login_failed", "User", null, null, new { reason = "unknown_user" });
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive || (user.LockoutEndUtc is { } lockEnd && lockEnd > now))
        {
            audit.Record("auth.login_blocked", "User", user.Id.ToString(), null, new { reason = user.IsActive ? "locked_out" : "inactive" });
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!passwords.Verify(user, request.Password, out var needsRehash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= securityOptions.Value.MaxFailedLogins)
            {
                user.LockoutEndUtc = now.AddMinutes(securityOptions.Value.LockoutMinutes);
                user.FailedLoginCount = 0;
            }

            audit.Record("auth.login_failed", "User", user.Id.ToString(), null, new { reason = "bad_password" });
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (needsRehash)
        {
            user.PasswordHash = passwords.Hash(user, request.Password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;

        var response = await IssueTokensAsync(user, now, ct);
        audit.Record("auth.login", "User", user.Id.ToString(), null);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 200)
        {
            throw new UnauthorizedException("Phiên đăng nhập không hợp lệ.");
        }

        var now = clock.UtcNow;
        var hash = SecureTokens.Sha256(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException("Phiên đăng nhập không hợp lệ.");

        if (token.RevokedAtUtc is not null)
        {
            // Reuse of a rotated token: revoke the whole family for safety.
            await db.RefreshTokens.Where(t => t.UserId == token.UserId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
            audit.Record("auth.refresh_reuse_detected", "User", token.UserId.ToString(), null);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("Phiên đăng nhập đã hết hạn.");
        }

        if (!token.IsActive(now))
        {
            throw new UnauthorizedException("Phiên đăng nhập đã hết hạn.");
        }

        var user = await db.Users.Include(u => u.BranchAssignments).FirstAsync(u => u.Id == token.UserId, ct);
        if (!user.IsActive || (user.LockoutEndUtc is { } lockEnd && lockEnd > now))
        {
            token.RevokedAtUtc = now;
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("Phiên đăng nhập đã hết hạn.");
        }

        var response = await IssueTokensAsync(user, now, ct);
        token.RevokedAtUtc = now;
        token.ReplacedByTokenHash = SecureTokens.Sha256(response.RefreshToken);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var hash = SecureTokens.Sha256(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null && token.RevokedAtUtc is null)
        {
            if (currentUser.UserId is { } uid && uid != token.UserId)
            {
                throw new ForbiddenException();
            }

            token.RevokedAtUtc = clock.UtcNow;
            audit.Record("auth.logout", "User", token.UserId.ToString(), null);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<MeResponse> MeAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().Include(u => u.BranchAssignments).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException();
        return await BuildMeAsync(user, ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        new Validator()
            .Required(request.CurrentPassword, "currentPassword")
            .Required(request.NewPassword, "newPassword")
            .ThrowIfInvalid();
        PasswordPolicy.Validate(request.NewPassword, securityOptions.Value.MinPasswordLength, "newPassword");

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (!passwords.Verify(user, request.CurrentPassword, out _))
        {
            throw ValidationFailedException.For("currentPassword", "Mật khẩu hiện tại không đúng.");
        }

        user.PasswordHash = passwords.Hash(user, request.NewPassword);
        user.RotateSecurityStamp();
        user.UpdatedAtUtc = clock.UtcNow;
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, clock.UtcNow), ct);
        audit.Record("auth.password_changed", "User", user.Id.ToString(), null);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, DateTime now, CancellationToken ct)
    {
        var branchIds = user.BranchAssignments.Select(a => a.BranchId).ToList();
        var access = tokens.CreateAccessToken(user, branchIds);
        var rawRefresh = SecureTokens.Create(48);
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = SecureTokens.Sha256(rawRefresh),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(jwtOptions.Value.RefreshTokenDays),
            CreatedByIp = currentUser.IpAddress,
        };
        db.RefreshTokens.Add(refresh);
        var me = await BuildMeAsync(user, ct);
        return new AuthResponse(access.Token, access.ExpiresAtUtc, rawRefresh, refresh.ExpiresAtUtc, me);
    }

    private async Task<MeResponse> BuildMeAsync(User user, CancellationToken ct)
    {
        var branchIds = user.BranchAssignments.Select(a => a.BranchId).ToList();
        var branchQuery = db.Branches.AsNoTracking();
        if (user.Role != UserRole.SuperAdmin)
        {
            branchQuery = branchQuery.Where(b => branchIds.Contains(b.Id));
        }

        var branches = await branchQuery.OrderBy(b => b.Name)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name))
            .ToListAsync(ct);
        var employeeId = await db.Employees.AsNoTracking().Where(e => e.UserId == user.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        return new MeResponse(user.Id, user.UserName, user.DisplayName, user.Role.ToString(), branches, employeeId);
    }
}

public static class PasswordPolicy
{
    public static void Validate(string password, int minLength, string field)
    {
        var v = new Validator();
        v.Must(password.Length >= minLength, field, $"Mật khẩu tối thiểu {minLength} ký tự.");
        v.Must(password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit), field,
            "Mật khẩu phải có chữ hoa, chữ thường và chữ số.");
        v.ThrowIfInvalid();
    }
}
