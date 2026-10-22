using Fnb.Contracts;
using Fnb.Domain.Identity;

namespace Fnb.Application.Abstractions;

public interface IMessagePublisher
{
    Task PublishAsync(string topic, MessageEnvelope envelope, CancellationToken ct);
}

/// <summary>Thrown by handlers when a message can never succeed (goes straight to dead-letter, no retries).</summary>
public sealed class PermanentMessageException(string message, Exception? inner = null) : Exception(message, inner);

public interface IMessageHandler
{
    /// <summary>Stable consumer name used for inbox de-duplication.</summary>
    string ConsumerName { get; }
    bool CanHandle(string messageType);
    Task HandleAsync(MessageEnvelope envelope, CancellationToken ct);
}

/// <summary>Subscribes a handler pipeline to a topic subscription (RabbitMQ queue / Service Bus subscription).</summary>
public interface IMessageSubscriber
{
    Task StartAsync(string topic, string subscription, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

/// <summary>Adds integration events to the outbox within the current DbContext unit of work.</summary>
public interface IOutbox
{
    void Enqueue<T>(string topic, string type, T payload, string? correlationId = null)
        where T : notnull;
}

public interface IAuditService
{
    /// <summary>Adds an audit entry to the current unit of work (persisted on SaveChanges).</summary>
    void Record(string action, string resourceType, string? resourceId, Guid? branchId, object? changes = null);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IReadOnlyCollection<Guid> branchIds);
}

public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User user, string password, out bool needsRehash);
    /// <summary>Performs an equivalent hashing workload for unknown users (timing equalization).</summary>
    void VerifyDummy(string password);
}

/// <summary>
/// Public order tracking tokens: token = base64url(HMAC-SHA256(secret, orderId)). Unguessable without the server secret,
/// not derivable from OrderId/OrderNumber, recomputable for idempotent replays, and only its SHA-256 hash is stored.
/// </summary>
public interface ITrackingTokenService
{
    string Create(Guid orderId);
    string Hash(string token);
}

public interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfModel model, InvoicePaperSize size);
}

public enum InvoicePaperSize
{
    K80 = 1,
    A5 = 2,
}

public sealed record InvoicePdfModel(
    string BrandName,
    string BranchName,
    string BranchAddress,
    string? BranchPhone,
    string InvoiceNo,
    int OrderNumber,
    DateTime IssuedAtLocal,
    string? TableName,
    IReadOnlyList<InvoicePdfLine> Lines,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TaxRatePercent,
    bool PricesIncludeTax,
    decimal RoundingAmount,
    decimal TotalAmount,
    string PaymentMethod,
    string PaymentStatus,
    string? Footer,
    string? TrackingUrl);

public sealed record InvoicePdfLine(string Name, string Variant, string? AddOns, string? Note, int Quantity, decimal UnitPrice, decimal LineTotal);
