using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Identity;
using Fnb.Infrastructure.Messaging;
using Fnb.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fnb.Api.Security;

public static class FnbClaims
{
    public const string Branch = "fnb_branch";
    public const string SecurityStamp = "fnb_sstamp";
}

public static class Policies
{
    public const string Staff = "staff";
    public const string BranchAdmin = "branch-admin";
    public const string SuperAdmin = "super-admin";
}

/// <summary>Issues short-lived HMAC-SHA256 JWT access tokens. Refresh tokens are opaque and handled by AuthService.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<Guid> branchIds)
    {
        var opt = options.Value;
        var now = clock.UtcNow;
        var expires = now.AddMinutes(opt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new("name", user.DisplayName),
            new("role", user.Role.ToString()),
            new(FnbClaims.SecurityStamp, user.SecurityStamp),
        };
        claims.AddRange(branchIds.Select(b => new Claim(FnbClaims.Branch, b.ToString())));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.SigningKey));
        var token = new JwtSecurityToken(opt.Issuer, opt.Audience, claims, now, expires, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static TokenValidationParameters ValidationParameters(JwtOptions opt) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = opt.Issuer,
        ValidateAudience = true,
        ValidAudience = opt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
        RoleClaimType = "role",
    };
}

/// <summary>
/// Rejects otherwise-valid access tokens whose user was deactivated or whose security stamp changed (password reset,
/// role/branch change). Cached for a short time to bound DB load; worst-case revocation latency = cache TTL.
/// </summary>
public static class TokenRevocationCheck
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null || !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail("invalid_subject");
            return;
        }

        var stamp = principal.FindFirstValue(FnbClaims.SecurityStamp);
        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        var state = await cache.GetOrCreateAsync($"user-state:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            return await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new UserState(u.IsActive, u.SecurityStamp)).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        });

        if (state is null || !state.IsActive || state.SecurityStamp != stamp)
        {
            context.Fail("token_revoked");
        }
    }

    public static void Invalidate(IMemoryCache cache, Guid userId) => cache.Remove($"user-state:{userId}");

    private sealed record UserState(bool IsActive, string SecurityStamp);
}

/// <summary>Current principal from the HTTP request (JWT claims), unless a scoped override (system user) is set.</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var http = accessor.HttpContext;
        IpAddress = http?.Connection.RemoteIpAddress?.ToString();
        CorrelationId = http?.TraceIdentifier;
        var principal = http?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        IsAuthenticated = true;
        UserId = Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        UserName = principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? principal.FindFirstValue("name") ?? "unknown";
        Role = Enum.TryParse<UserRole>(principal.FindFirstValue("role") ?? principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;
        BranchIds = principal.FindAll(FnbClaims.Branch).Select(c => Guid.TryParse(c.Value, out var b) ? b : Guid.Empty).Where(b => b != Guid.Empty).ToHashSet();
    }

    public bool IsAuthenticated { get; }
    public Guid? UserId { get; }
    public string UserName { get; } = "anonymous";
    public UserRole? Role { get; }
    public IReadOnlyCollection<Guid> BranchIds { get; } = [];
    public string? IpAddress { get; }
    public string? CorrelationId { get; }
    public bool IsSystem => false;
}

public static class CurrentUserRegistration
{
    public static IServiceCollection AddHttpCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser>(sp =>
            sp.GetRequiredService<CurrentUserContext>().Override
            ?? new HttpCurrentUser(sp.GetRequiredService<IHttpContextAccessor>()));
        return services;
    }
}
