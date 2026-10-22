using System.Security.Claims;
using Fnb.Api.Security;
using Fnb.Application.Abstractions;
using Fnb.Contracts;
using Fnb.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Api.Realtime;

public static class RealtimeGroups
{
    public static string Staff(Guid branchId) => $"branch:{branchId:N}:staff";
    public static string Queue(Guid branchId) => $"branch:{branchId:N}:queue";
    public static string Tracking(Guid orderId) => $"order:{orderId:N}:tracking";
}

/// <summary>
/// Realtime order hub. Clients must explicitly join a group; every join is authorized server-side:
/// staff groups require a JWT with access to the branch; queue display uses the public branch code and only ever
/// receives order numbers + statuses; tracking requires the unguessable tracking token.
/// Clients should reload a REST snapshot after (re)connecting — the hub does not replay missed events.
/// </summary>
[AllowAnonymous]
public sealed class OrdersHub(AppDbContext db, ITrackingTokenService trackingTokens, ILogger<OrdersHub> logger) : Hub
{
    public async Task JoinBranchStaff(Guid branchId)
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new HubException("unauthorized");
        }

        var isSuper = user.FindFirstValue("role") == "SuperAdmin";
        var allowed = isSuper || user.FindAll(FnbClaims.Branch).Any(c => c.Value == branchId.ToString());
        if (!allowed)
        {
            logger.LogWarning("Hub join denied: user {User} → branch {Branch}", user.FindFirstValue("sub"), branchId);
            throw new HubException("forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Staff(branchId));
    }

    public async Task LeaveBranchStaff(Guid branchId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Staff(branchId));

    public async Task<Guid> JoinQueueDisplay(string branchCode)
    {
        var code = (branchCode ?? "").Trim().ToUpperInvariant();
        var branchId = await db.Branches.AsNoTracking().Where(b => b.Code == code && b.IsActive).Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
        if (branchId is null)
        {
            throw new HubException("not_found");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Queue(branchId.Value));
        return branchId.Value;
    }

    public async Task JoinOrderTracking(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            throw new HubException("not_found");
        }

        var hash = trackingTokens.Hash(token);
        var orderId = await db.Orders.AsNoTracking().Where(o => o.PublicTrackingTokenHash == hash).Select(o => (Guid?)o.Id).FirstOrDefaultAsync();
        if (orderId is null)
        {
            throw new HubException("not_found");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Tracking(orderId.Value));
    }
}

/// <summary>API-side consumer of the "realtime" subscription: relays committed order events to SignalR groups.</summary>
public sealed class RealtimeRelayHandler(IHubContext<OrdersHub> hub) : IMessageHandler
{
    public string ConsumerName => Subscriptions.Realtime;

    public bool CanHandle(string messageType) =>
        messageType is MessageTypes.OrderSubmitted or MessageTypes.OrderStatusChanged or MessageTypes.OrderPaymentChanged;

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        switch (envelope.Type)
        {
            case MessageTypes.OrderSubmitted:
                {
                    var e = envelope.Deserialize<OrderSubmittedV1>();
                    await hub.Clients.Group(RealtimeGroups.Staff(e.BranchId)).SendAsync("orderCreated",
                        new { e.OrderId, e.OrderNumber, e.BusinessDate, status = "Submitted" }, ct);
                    await hub.Clients.Group(RealtimeGroups.Queue(e.BranchId)).SendAsync("queueChanged",
                        new { e.OrderNumber, e.BusinessDate, status = "Submitted" }, ct);
                    break;
                }

            case MessageTypes.OrderStatusChanged:
                {
                    var e = envelope.Deserialize<OrderStatusChangedV1>();
                    await hub.Clients.Group(RealtimeGroups.Staff(e.BranchId)).SendAsync("orderStatusChanged",
                        new { e.OrderId, e.OrderNumber, e.BusinessDate, e.FromStatus, e.ToStatus, e.ChangedAtUtc }, ct);
                    await hub.Clients.Group(RealtimeGroups.Queue(e.BranchId)).SendAsync("queueChanged",
                        new { e.OrderNumber, e.BusinessDate, status = e.ToStatus }, ct);
                    await hub.Clients.Group(RealtimeGroups.Tracking(e.OrderId)).SendAsync("orderStatusChanged",
                        new { e.OrderId, status = e.ToStatus, e.ChangedAtUtc }, ct);
                    break;
                }

            case MessageTypes.OrderPaymentChanged:
                {
                    var e = envelope.Deserialize<OrderPaymentChangedV1>();
                    await hub.Clients.Group(RealtimeGroups.Staff(e.BranchId)).SendAsync("paymentChanged",
                        new { e.OrderId, e.OrderNumber, e.PaymentStatus }, ct);
                    await hub.Clients.Group(RealtimeGroups.Tracking(e.OrderId)).SendAsync("paymentChanged",
                        new { e.OrderId, e.PaymentStatus }, ct);
                    break;
                }
        }
    }
}
