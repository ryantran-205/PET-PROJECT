using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fnb.Infrastructure.Observability;

/// <summary>Custom metrics (System.Diagnostics.Metrics). Exported via OpenTelemetry/Azure Monitor when configured, or dotnet-counters locally.</summary>
public static class FnbMetrics
{
    public const string MeterName = "Fnb";
    public const string ActivitySourceName = "Fnb";

    private static readonly Meter Meter = new(MeterName, "1.0");
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("fnb.outbox.published");
    public static readonly Counter<long> OutboxPublishFailed = Meter.CreateCounter<long>("fnb.outbox.publish_failed");
    public static readonly Counter<long> MessagesProcessed = Meter.CreateCounter<long>("fnb.messages.processed");
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("fnb.messages.failed");
    public static readonly Counter<long> MessagesDeadLettered = Meter.CreateCounter<long>("fnb.messages.dead_lettered");
    public static readonly Histogram<double> MessageProcessingMs = Meter.CreateHistogram<double>("fnb.messages.processing_ms", "ms");
    public static readonly Counter<long> CacheFailures = Meter.CreateCounter<long>("fnb.cache.failures");
}
