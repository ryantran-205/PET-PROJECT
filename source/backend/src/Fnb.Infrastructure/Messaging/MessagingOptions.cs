namespace Fnb.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    /// <summary>InMemory (single-process dev only, not durable) | RabbitMQ (local) | AzureServiceBus (cloud).</summary>
    public string Provider { get; set; } = "InMemory";

    public int MaxDeliveryAttempts { get; set; } = 5;
    public int BaseRetryDelayMs { get; set; } = 500;
    public int MaxRetryDelayMs { get; set; } = 30_000;
    public int PrefetchCount { get; set; } = 10;

    public RabbitMqOptions RabbitMq { get; set; } = new();
    public ServiceBusOptions AzureServiceBus { get; set; } = new();

    public TimeSpan RetryDelay(int attempt)
    {
        var ms = Math.Min(MaxRetryDelayMs, BaseRetryDelayMs * Math.Pow(2, Math.Max(0, attempt - 1)));
        return TimeSpan.FromMilliseconds(ms + Random.Shared.Next(0, 250));
    }
}

public sealed class RabbitMqOptions
{
    /// <summary>amqp://user:pass@host:5672/vhost — provide via environment/user-secrets.</summary>
    public string ConnectionString { get; set; } = "";
    public string ExchangePrefix { get; set; } = "fnb";
}

public sealed class ServiceBusOptions
{
    /// <summary>Either a connection string (local/dev) or FullyQualifiedNamespace with Managed Identity (recommended).</summary>
    public string? ConnectionString { get; set; }
    public string? FullyQualifiedNamespace { get; set; }
    public int MaxConcurrentCalls { get; set; } = 4;
}

public sealed class OutboxOptions
{
    public const string Section = "Outbox";
    public int PollIntervalMs { get; set; } = 1000;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
    public int LockSeconds { get; set; } = 30;
}

public sealed class RetentionOptions
{
    public const string Section = "Retention";
    public int PublishedOutboxDays { get; set; } = 7;
    public int ProcessedMessageDays { get; set; } = 14;
    public int AuditLogDays { get; set; } = 365;
    public int RevokedRefreshTokenDays { get; set; } = 30;
}
