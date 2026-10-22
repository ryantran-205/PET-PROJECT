using Fnb.Application.Abstractions;
using Fnb.Application.Inventory;
using Fnb.Contracts;
using Fnb.Domain.Common;
using Fnb.Domain.Inventory;
using Fnb.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fnb.Application.Orders;

/// <summary>
/// Worker-side consumer for order events. Idempotent by (MessageId, consumer) inbox records written in the same
/// transaction as the business change, plus state checks — redelivered messages never create duplicate effects.
/// </summary>
public sealed class OrderProcessingHandler(
    IAppDbContext db,
    IClock clock,
    IOutbox outbox,
    InvoiceService invoices,
    IUniqueConstraintDetector uniqueDetector,
    ILogger<OrderProcessingHandler> logger) : IMessageHandler
{
    public string ConsumerName => Subscriptions.OrderProcessing;

    public bool CanHandle(string messageType) => messageType is MessageTypes.OrderSubmitted or MessageTypes.OrderStatusChanged;

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AsNoTracking().AnyAsync(p => p.MessageId == envelope.MessageId && p.Consumer == ConsumerName, ct))
        {
            logger.LogInformation("Duplicate message {MessageId} ignored by {Consumer}", envelope.MessageId, ConsumerName);
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        switch (envelope.Type)
        {
            case MessageTypes.OrderSubmitted:
                await OnSubmittedAsync(envelope.Deserialize<OrderSubmittedV1>(), envelope.CorrelationId, ct);
                break;
            case MessageTypes.OrderStatusChanged:
                await OnStatusChangedAsync(envelope.Deserialize<OrderStatusChangedV1>(), ct);
                break;
        }

        db.ProcessedMessages.Add(new ProcessedMessage { MessageId = envelope.MessageId, Consumer = ConsumerName, ProcessedAtUtc = clock.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (uniqueDetector.IsUniqueViolation(ex))
        {
            // Another worker instance processed the same message concurrently; its transaction won.
            logger.LogInformation("Message {MessageId} processed concurrently by another instance", envelope.MessageId);
        }
    }

    /// <summary>
    /// Submitted → PendingConfirmation. Performs a non-binding stock check and flags the order for staff attention if
    /// stock looks insufficient (the binding check happens atomically at confirmation).
    /// </summary>
    private async Task OnSubmittedAsync(OrderSubmittedV1 evt, string correlationId, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).ThenInclude(i => i.AddOns).Include(o => o.StatusHistory).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == evt.OrderId, ct);
        if (order is null)
        {
            throw new PermanentMessageException($"Order {evt.OrderId} not found.");
        }

        if (order.Status != OrderStatus.Submitted)
        {
            return; // Already handled or moved on by staff — nothing to do.
        }

        var variantIds = order.Items.Select(i => i.VariantId).Distinct().ToList();
        var addOnIds = order.Items.SelectMany(i => i.AddOns.Select(a => a.AddOnId)).Distinct().ToList();
        var recipe = await db.RecipeLines.AsNoTracking()
            .Where(l => (l.VariantId != null && variantIds.Contains(l.VariantId.Value)) || (l.AddOnId != null && addOnIds.Contains(l.AddOnId.Value)))
            .ToListAsync(ct);
        var required = RecipeCalculator.Requirements(
            order.Items.Select(i => new ConsumptionLine(i.VariantId, i.AddOns.Select(a => a.AddOnId).ToList(), i.Quantity)),
            recipe.Where(l => l.VariantId != null).GroupBy(l => l.VariantId!.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<RecipeComponent>)g.Select(l => new RecipeComponent(l.IngredientId, l.Quantity)).ToList()),
            recipe.Where(l => l.AddOnId != null).GroupBy(l => l.AddOnId!.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<RecipeComponent>)g.Select(l => new RecipeComponent(l.IngredientId, l.Quantity)).ToList()));
        var ingredientIds = required.Keys.ToList();
        var available = await db.BranchInventories.AsNoTracking()
            .Where(b => b.BranchId == order.BranchId && ingredientIds.Contains(b.IngredientId))
            .ToDictionaryAsync(b => b.IngredientId, b => b.QuantityOnHand, ct);
        var shortfalls = RecipeCalculator.Shortfalls(required, available);
        if (shortfalls.Count > 0)
        {
            var names = await db.Ingredients.AsNoTracking().Where(i => shortfalls.Keys.Contains(i.Id)).Select(i => i.Name).ToListAsync(ct);
            order.NeedsAttention = true;
            order.AttentionNote = $"Có thể thiếu nguyên liệu: {string.Join(", ", names)}";
        }

        var now = clock.UtcNow;
        order.TransitionTo(OrderStatus.PendingConfirmation, OrderActor.System, null, null, now);
        outbox.Enqueue(Topics.OrderEvents, MessageTypes.OrderStatusChanged,
            new OrderStatusChangedV1(order.Id, order.BranchId, order.OrderNumber, order.BusinessDate,
                OrderStatus.Submitted.ToString(), OrderStatus.PendingConfirmation.ToString(), now),
            correlationId);
    }

    /// <summary>On Confirmed: create the internal invoice snapshot (idempotent; one invoice per order).</summary>
    private async Task OnStatusChangedAsync(OrderStatusChangedV1 evt, CancellationToken ct)
    {
        if (evt.ToStatus != nameof(OrderStatus.Confirmed))
        {
            return;
        }

        var status = await db.Orders.AsNoTracking().Where(o => o.Id == evt.OrderId).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync(ct);
        if (status is null or OrderStatus.Cancelled or OrderStatus.Rejected)
        {
            return;
        }

        await invoices.EnsureForOrderAsync(evt.OrderId, ct);
    }
}
