using System.Diagnostics;
using Fnb.Application.Abstractions;
using Fnb.Contracts;
using Fnb.Infrastructure.Observability;
using Fnb.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fnb.Infrastructure.Messaging;

/// <summary>
/// Scoped holder that lets background work run as <see cref="SystemUser"/> (or any principal) inside a DI scope.
/// Hosts register <see cref="ICurrentUser"/> as "override ?? default principal".
/// </summary>
public sealed class CurrentUserContext
{
    public ICurrentUser? Override { get; set; }
}

/// <summary>
/// Transactional outbox dispatcher. Rows are claimed with UPDLOCK/READPAST so multiple worker instances never publish
/// the same row concurrently; a lease (LockedUntilUtc) recovers rows from crashed instances. Delivery is at-least-once:
/// a crash between publish and mark-published causes a re-publish, which consumers de-duplicate by MessageId.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    IOptions<MessagingOptions> messagingOptions,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const string ClaimSql = """
        UPDATE TOP (@batch) o
        SET o.LockedUntilUtc = @lockUntil
        OUTPUT inserted.Id AS [Value]
        FROM [OutboxMessages] o WITH (ROWLOCK, READPAST, UPDLOCK)
        WHERE o.PublishedAtUtc IS NULL AND o.Failed = 0 AND o.NextAttemptAtUtc <= @now
          AND (o.LockedUntilUtc IS NULL OR o.LockedUntilUtc < @now);
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        logger.LogInformation("Outbox dispatcher started (batch {Batch}, poll {Poll} ms)", opt.BatchSize, opt.PollIntervalMs);
        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatched = 0;
            try
            {
                dispatched = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed; will retry");
            }

            if (dispatched < opt.BatchSize)
            {
                try
                {
                    await Task.Delay(opt.PollIntervalMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        var opt = options.Value;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var ids = await db.Database.SqlQueryRaw<Guid>(ClaimSql,
                new SqlParameter("@batch", opt.BatchSize),
                new SqlParameter("@lockUntil", now.AddSeconds(opt.LockSeconds)),
                new SqlParameter("@now", now))
            .ToListAsync(ct);
        if (ids.Count == 0)
        {
            return 0;
        }

        var messages = await db.OutboxMessages.Where(m => ids.Contains(m.Id)).OrderBy(m => m.OccurredAtUtc).ToListAsync(ct);
        foreach (var message in messages)
        {
            using var activity = FnbMetrics.ActivitySource.StartActivity("outbox.publish", ActivityKind.Producer);
            activity?.SetTag("messaging.message.id", message.Id);
            activity?.SetTag("messaging.message.type", message.Type);
            try
            {
                var envelope = new MessageEnvelope(message.Id, message.Type, message.CorrelationId ?? message.Id.ToString("N"),
                    message.OccurredAtUtc, 1, message.Payload);
                await publisher.PublishAsync(message.Topic, envelope, ct);
                message.PublishedAtUtc = DateTime.UtcNow;
                message.LockedUntilUtc = null;
                message.LastError = null;
                FnbMetrics.OutboxPublished.Add(1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LockedUntilUtc = null;
                message.LastError = Truncate(ex.Message, 2000);
                if (message.Attempts >= opt.MaxAttempts)
                {
                    message.Failed = true;
                    logger.LogError(ex, "Outbox message {MessageId} ({Type}) marked Failed after {Attempts} attempts", message.Id, message.Type, message.Attempts);
                }
                else
                {
                    message.NextAttemptAtUtc = DateTime.UtcNow.Add(messagingOptions.Value.RetryDelay(message.Attempts));
                    logger.LogWarning(ex, "Outbox message {MessageId} publish failed (attempt {Attempts})", message.Id, message.Attempts);
                }

                FnbMetrics.OutboxPublishFailed.Add(1);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return messages.Count;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Subscribes to one topic subscription and dispatches each message to the registered <see cref="IMessageHandler"/>s whose
/// <see cref="IMessageHandler.ConsumerName"/> matches. Each message runs in its own DI scope as the system user.
/// </summary>
public sealed class MessageConsumerHostedService(
    string topic,
    string subscription,
    IMessageSubscriber subscriber,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageConsumerHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting consumer {Subscription} on topic {Topic}", subscription, topic);
        return subscriber.StartAsync(topic, subscription, HandleAsync, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => subscriber.StopAsync(cancellationToken);

    private async Task HandleAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        using var activity = FnbMetrics.ActivitySource.StartActivity($"consume {subscription}", ActivityKind.Consumer);
        activity?.SetTag("messaging.message.id", envelope.MessageId);
        activity?.SetTag("messaging.message.type", envelope.Type);
        activity?.SetTag("fnb.correlation_id", envelope.CorrelationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = envelope.CorrelationId, ["MessageId"] = envelope.MessageId });

        var sw = Stopwatch.StartNew();
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().Override = new SystemUser { CorrelationId = envelope.CorrelationId };
        var handlers = scope.ServiceProvider.GetServices<IMessageHandler>()
            .Where(h => h.ConsumerName == subscription && h.CanHandle(envelope.Type))
            .ToList();
        try
        {
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(envelope, ct);
            }

            FnbMetrics.MessagesProcessed.Add(1, new KeyValuePair<string, object?>("subscription", subscription));
        }
        catch (Exception)
        {
            FnbMetrics.MessagesFailed.Add(1, new KeyValuePair<string, object?>("subscription", subscription));
            throw;
        }
        finally
        {
            FnbMetrics.MessageProcessingMs.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("subscription", subscription));
        }
    }
}

/// <summary>Periodic retention cleanup for technical tables (outbox, inbox, idempotency, refresh tokens, audit).</summary>
public sealed class MaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<MaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var now = DateTime.UtcNow;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var outboxCutoff = now.AddDays(-opt.PublishedOutboxDays);
        var outbox = await db.OutboxMessages.Where(m => m.PublishedAtUtc != null && m.PublishedAtUtc < outboxCutoff).ExecuteDeleteAsync(ct);
        var inboxCutoff = now.AddDays(-opt.ProcessedMessageDays);
        var inbox = await db.ProcessedMessages.Where(m => m.ProcessedAtUtc < inboxCutoff).ExecuteDeleteAsync(ct);
        var idem = await db.IdempotencyRecords.Where(r => r.ExpiresAtUtc < now).ExecuteDeleteAsync(ct);
        var tokenCutoff = now.AddDays(-opt.RevokedRefreshTokenDays);
        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAtUtc < tokenCutoff || (t.RevokedAtUtc != null && t.RevokedAtUtc < tokenCutoff)).ExecuteDeleteAsync(ct);
        var audit = 0;
        if (opt.AuditLogDays > 0)
        {
            var auditCutoff = now.AddDays(-opt.AuditLogDays);
            audit = await db.AuditLogs.Where(a => a.TimestampUtc < auditCutoff).ExecuteDeleteAsync(ct);
        }

        if (outbox + inbox + idem + tokens + audit > 0)
        {
            logger.LogInformation("Retention cleanup removed outbox={Outbox} inbox={Inbox} idempotency={Idem} refreshTokens={Tokens} audit={Audit}",
                outbox, inbox, idem, tokens, audit);
        }
    }
}
