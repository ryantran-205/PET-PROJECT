using Fnb.Api.Infrastructure;
using Fnb.Api.Security;
using Fnb.Application;
using Fnb.Application.Auth;
using Fnb.Application.Branches;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Fnb.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/login", (LoginRequest request, AuthService svc, CancellationToken ct) => svc.LoginAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Login)
            .WithSummary("Đăng nhập, trả access token (JWT) + refresh token.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/refresh", (RefreshRequest request, AuthService svc, CancellationToken ct) => svc.RefreshAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Login)
            .WithSummary("Xoay vòng refresh token (phát hiện tái sử dụng → thu hồi toàn bộ).")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/logout", async (RefreshRequest request, AuthService svc, CancellationToken ct) =>
            {
                await svc.LogoutAsync(request, ct);
                return TypedResults.NoContent();
            })
            .AllowAnonymous().WithSummary("Thu hồi refresh token.");

        var me = app.MapGroup("/me").WithTags("Auth").RequireAuthorization(Policies.Staff);
        me.MapGet("/", (AuthService svc, CancellationToken ct) => svc.MeAsync(ct)).WithSummary("Thông tin người dùng hiện tại và chi nhánh được gán.");
        me.MapPost("/change-password", async (ChangePasswordRequest request, AuthService svc, CancellationToken ct) =>
        {
            await svc.ChangePasswordAsync(request, ct);
            return TypedResults.NoContent();
        }).WithSummary("Đổi mật khẩu (thu hồi các phiên khác).");

        var users = app.MapGroup("/users").WithTags("Users").RequireAuthorization(Policies.BranchAdmin);
        users.MapGet("/", ([AsParameters] UserQuery query, UserService svc, CancellationToken ct) => svc.ListAsync(query, ct));
        users.MapPost("/", async Task<Created<UserDto>> (CreateUserRequest request, UserService svc, CancellationToken ct) =>
        {
            var user = await svc.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/users/{user.Id}", user);
        });
        users.MapPut("/{userId:guid}", (Guid userId, UpdateUserRequest request, UserService svc, CancellationToken ct) => svc.UpdateAsync(userId, request, ct));
        users.MapPost("/{userId:guid}/reset-password", async (Guid userId, ResetPasswordRequest request, UserService svc, CancellationToken ct) =>
        {
            await svc.ResetPasswordAsync(userId, request, ct);
            return TypedResults.NoContent();
        });

        app.MapGet("/audit-logs", ([AsParameters] AuditQuery query, AuditQueryService svc, CancellationToken ct) => svc.ListAsync(query, ct))
            .WithTags("Audit").RequireAuthorization(Policies.BranchAdmin);
        return app;
    }
}

public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/branches").WithTags("Branches").RequireAuthorization(Policies.Staff);
        g.MapGet("/", (BranchService svc, CancellationToken ct) => svc.ListAsync(ct)).WithSummary("Danh sách chi nhánh trong phạm vi của người dùng.");
        g.MapPost("/", async Task<Created<BranchDto>> (CreateBranchRequest request, BranchService svc, CancellationToken ct) =>
        {
            var branch = await svc.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/branches/{branch.Id}", branch);
        }).RequireAuthorization(Policies.SuperAdmin);
        g.MapGet("/{branchId:guid}", (Guid branchId, BranchService svc, CancellationToken ct) => svc.GetAsync(branchId, ct));
        g.MapPut("/{branchId:guid}", (Guid branchId, UpdateBranchRequest request, BranchService svc, CancellationToken ct) => svc.UpdateAsync(branchId, request, ct))
            .RequireAuthorization(Policies.BranchAdmin);
        g.MapPost("/{branchId:guid}/tables", async Task<Created<BranchTableDto>> (Guid branchId, CreateTableRequest request, BranchService svc, CancellationToken ct) =>
        {
            var table = await svc.AddTableAsync(branchId, request, ct);
            return TypedResults.Created($"/api/v1/branches/{branchId}/tables/{table.Id}", table);
        }).RequireAuthorization(Policies.BranchAdmin);
        g.MapPut("/{branchId:guid}/tables/{tableId:guid}", (Guid branchId, Guid tableId, UpdateTableRequest request, BranchService svc, CancellationToken ct) =>
            svc.UpdateTableAsync(branchId, tableId, request, ct)).RequireAuthorization(Policies.BranchAdmin);
        return app;
    }
}
