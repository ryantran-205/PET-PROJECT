using Fnb.Application.Abstractions;
using Fnb.Application.Auth;
using Fnb.Application.Branches;
using Fnb.Application.Common;
using Fnb.Application.Hr;
using Fnb.Application.Inventory;
using Fnb.Application.Menu;
using Fnb.Application.Orders;
using Fnb.Application.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fnb.Application;

public sealed record AuditLogDto(Guid Id, Guid? ActorUserId, string ActorName, string Action, string ResourceType, string? ResourceId, Guid? BranchId,
    DateTime TimestampUtc, string? CorrelationId, string? IpAddress, string? Changes);

public sealed record AuditQuery(Guid? BranchId, string? Action, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 50);

public sealed class AuditQueryService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct)
    {
        if (!currentUser.IsBranchAdminOrAbove())
        {
            throw new Domain.Common.ForbiddenException();
        }

        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        var branches = currentUser.ResolveBranchFilter(query.BranchId);
        if (branches is not null)
        {
            q = q.Where(a => a.BranchId != null && branches.Contains(a.BranchId.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(a => a.Action.StartsWith(query.Action));
        }

        if (query.FromUtc is { } from)
        {
            q = q.Where(a => a.TimestampUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            q = q.Where(a => a.TimestampUtc < to);
        }

        var p = new PageRequest(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.TimestampUtc).Skip(p.Skip).Take(p.SafePageSize)
            .Select(a => new AuditLogDto(a.Id, a.ActorUserId, a.ActorName, a.Action, a.ResourceType, a.ResourceId, a.BranchId, a.TimestampUtc,
                a.CorrelationId, a.IpAddress, a.Changes))
            .ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, p.SafePage, p.SafePageSize, total);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<BranchService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<ShiftService>();
        services.AddScoped<LeaveService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<PayrollService>();
        services.AddScoped<MenuService>();
        services.AddScoped<PublicMenuService>();
        services.AddScoped<InventoryLedgerWriter>();
        services.AddScoped<InventoryService>();
        services.AddScoped<PublicOrderService>();
        services.AddScoped<OrderService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<OrderProcessingHandler>();
        return services;
    }
}
