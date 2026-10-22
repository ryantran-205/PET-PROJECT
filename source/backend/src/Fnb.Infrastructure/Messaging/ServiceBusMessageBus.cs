using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Fnb.Application.Abstractions;
using Fnb.Contracts;
using Fnb.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fnb.Infrastructure.Messaging;

/// <summary>
/// Azure Service Bus adapter (production). Topics/subscriptions are provisioned by infrastructure-as-code (infra/main.bicep)
/// with MaxDeliveryCount and dead-lettering enabled. Prefers Managed Identity (FullyQualifiedNamespace) over connection strings.
/// </summary>
public sealed class ServiceBusMessageBus : IMessagePublisher, IMessageSubscriber, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly MessagingOptions _options;
    private readonly ILogger<ServiceBusMessageBus> _logger;
    private readonly Dictionary<string, ServiceBusSender> _senders = [];
    private readonly List<ServiceBusProcessor> _processors = [];
    private readonly Lock _gate = new();

    public ServiceBusMessageBus(IOptions<MessagingOptions> options, ILogger<ServiceBusMessageBus> logger)
    {
        _options = options.Value;
        _logger = logger;
        var sb = _options.AzureServiceBus;
        _client = !string.IsNullOrWhiteSpace(sb.ConnectionString)
            ? new ServiceBusClient(sb.ConnectionString)
            : new ServiceBusClient(sb.FullyQualifiedNamespace ?? throw new InvalidOperationException("Messaging:AzureServiceBus:FullyQualifiedNamespace is required."),
                new DefaultAzureCredential());
    }

    public async Task PublishAsync(string topic, MessageEnvelope envelope, CancellationToken ct)
    {
        ServiceBusSender sender;
        lock (_gate)
        {
            if (!_senders.TryGetValue(topic, out sender!))
            {
                sender = _client.CreateSender(topic);
                _senders[topic] = sender;
            }
        }

        var message = new ServiceBusMessage(BinaryData.FromString(envelope.Payload))
        {
            MessageId = envelope.MessageId.ToString(),
            CorrelationId = envelope.CorrelationId,
            Subject = envelope.Type,
            ContentType = "application/json",
        };
        message.ApplicationProperties["occurredAtUtc"] = envelope.OccurredAtUtc;
        message.ApplicationProperties["schemaVersion"] = envelope.SchemaVersion;
        await sender.SendMessageAsync(message, ct);
    }

    public async Task StartAsync(string topic, string subscription, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken ct)
    {
        var processor = _client.CreateProcessor(topic, subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = _options.AzureServiceBus.MaxConcurrentCalls,
            PrefetchCount = _options.PrefetchCount,
        });
        processor.ProcessMessageAsync += async args =>
        {
            var m = args.Message;
            var envelope = new MessageEnvelope(
                Guid.TryParse(m.MessageId, out var id) ? id : Guid.Empty,
                m.Subject ?? "",
                m.CorrelationId ?? "",
                m.ApplicationProperties.TryGetValue("occurredAtUtc", out var o) && o is DateTime dt ? dt : m.EnqueuedTime.UtcDateTime,
                m.ApplicationProperties.TryGetValue("schemaVersion", out var s) && s is int si ? si : 1,
                m.Body.ToString());
            try
            {
                if (envelope.MessageId == Guid.Empty)
                {
                    throw new PermanentMessageException("Missing or invalid MessageId");
                }

                await handler(envelope, args.CancellationToken);
                await args.CompleteMessageAsync(m, args.CancellationToken);
            }
            catch (Exception ex)
            {
                if (FailurePolicy.Decide(ex, m.DeliveryCount, _options.MaxDeliveryAttempts) == FailureAction.DeadLetter)
                {
                    FnbMetrics.MessagesDeadLettered.Add(1);
                    _logger.LogError(ex, "Message {MessageId} ({Type}) dead-lettered after {Attempt} deliveries", m.MessageId, m.Subject, m.DeliveryCount);
                    await args.DeadLetterMessageAsync(m, ex.GetType().Name, Truncate(ex.Message, 1000), args.CancellationToken);
                }
                else
                {
                    var delay = _options.RetryDelay(m.DeliveryCount);
                    _logger.LogWarning(ex, "Message {MessageId} failed (delivery {Attempt}); abandoning after {Delay}", m.MessageId, m.DeliveryCount, delay);
                    await Task.Delay(delay, args.CancellationToken);
                    await args.AbandonMessageAsync(m, cancellationToken: args.CancellationToken);
                }
            }
        };
        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Service Bus processor error on {EntityPath} ({Source})", args.EntityPath, args.ErrorSource);
            return Task.CompletedTask;
        };
        _processors.Add(processor);
        await processor.StartProcessingAsync(ct);
        _logger.LogInformation("Service Bus processor started on {Topic}/{Subscription}", topic, subscription);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        foreach (var p in _processors)
        {
            await p.StopProcessingAsync(ct);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _processors)
        {
            await p.DisposeAsync();
        }

        foreach (var s in _senders.Values)
        {
            await s.DisposeAsync();
        }

        await _client.DisposeAsync();
    }
}
