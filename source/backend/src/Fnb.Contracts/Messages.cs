using System.Text.Json;

namespace Fnb.Contracts;

/// <summary>Transport envelope for all integration messages (at-least-once delivery; consumers must be idempotent).</summary>
public sealed record MessageEnvelope(
    Guid MessageId,
    string Type,
    string CorrelationId,
    DateTime OccurredAtUtc,
    int SchemaVersion,
    string Payload)
{
    public T Deserialize<T>() =>
        JsonSerializer.Deserialize<T>(Payload, MessagingJson.Options)
        ?? throw new InvalidOperationException($"Message {MessageId} payload could not be deserialized as {typeof(T).Name}.");
}

public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class Topics
{
    public const string OrderEvents = "order-events";
}

public static class Subscriptions
{
    /// <summary>Worker: business processing (validation, invoices).</summary>
    public const string OrderProcessing = "order-processing";
    /// <summary>API: relays events to SignalR clients.</summary>
    public const string Realtime = "realtime";
}

public static class MessageTypes
{
    public const string OrderSubmitted = "order.submitted.v1";
    public const string OrderStatusChanged = "order.status-changed.v1";
    public const string OrderPaymentChanged = "order.payment-changed.v1";
}

/// <summary>Raised when a new order is accepted. Only identifiers, no personal data.</summary>
public sealed record OrderSubmittedV1(Guid OrderId, Guid BranchId, int OrderNumber, DateOnly BusinessDate);

public sealed record OrderStatusChangedV1(
    Guid OrderId,
    Guid BranchId,
    int OrderNumber,
    DateOnly BusinessDate,
    string FromStatus,
    string ToStatus,
    DateTime ChangedAtUtc);

public sealed record OrderPaymentChangedV1(Guid OrderId, Guid BranchId, int OrderNumber, string PaymentStatus);
