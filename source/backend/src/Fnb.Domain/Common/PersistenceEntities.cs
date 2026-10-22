namespace Fnb.Domain.Common;

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    /// <summary>JSON of relevant changes. Never contains secrets.</summary>
    public string? Changes { get; set; }
}

public sealed class IdempotencyRecord
{
    /// <summary>Scoped key, e.g. "public-order:{client key}".</summary>
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public Guid? ResourceId { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Type { get; set; }
    public required string Topic { get; set; }
    public required string Payload { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LastError { get; set; }
    /// <summary>Set when the message exceeded max publish attempts; requires manual action.</summary>
    public bool Failed { get; set; }
}

public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public required string Consumer { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
