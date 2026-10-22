using System.Text;
using Fnb.Application.Abstractions;
using Fnb.Contracts;
using Fnb.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Fnb.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ adapter (local/dev). Topology per topic: fanout exchange "{prefix}.{topic}", durable queue per subscription
/// "{prefix}.{topic}.{subscription}" and a dead-letter queue "{...}.dlq". Publisher confirms + persistent messages.
/// Retries: failed deliveries are re-published with an incremented x-delivery-attempt header after an exponential backoff;
/// after MaxDeliveryAttempts (or a permanent error) they are moved to the DLQ.
/// </summary>
public sealed class RabbitMqMessageBus(IOptions<MessagingOptions> options, ILogger<RabbitMqMessageBus> logger)
    : IMessagePublisher, IMessageSubscriber, IAsyncDisposable
{
    private const string AttemptHeader = "x-delivery-attempt";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<IChannel> _consumerChannels = [];
    private IConnection? _connection;
    private IChannel? _publishChannel;

    private MessagingOptions Opt => options.Value;

    private string Exchange(string topic) => $"{Opt.RabbitMq.ExchangePrefix}.{topic}";

    private string Queue(string topic, string subscription) => $"{Opt.RabbitMq.ExchangePrefix}.{topic}.{subscription}";

    private async Task<IConnection> ConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(Opt.RabbitMq.ConnectionString),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = "fnb",
            };
            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task DeclareTopologyAsync(IChannel ch, string exchange, string? queue, CancellationToken ct)
    {
        await ch.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        if (queue is null)
        {
            return;
        }

        await ch.QueueDeclareAsync(queue + ".dlq", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await ch.QueueBindAsync(queue, exchange, routingKey: "", cancellationToken: ct);
    }

    public async Task PublishAsync(string topic, MessageEnvelope envelope, CancellationToken ct)
    {
        var connection = await ConnectionAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            _publishChannel ??= await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
            await DeclareTopologyAsync(_publishChannel, Exchange(topic), null, ct);
            await _publishChannel.BasicPublishAsync(Exchange(topic), "", mandatory: false, BuildProperties(envelope, 1),
                Encoding.UTF8.GetBytes(envelope.Payload), ct);
        }
        catch
        {
            // Force re-creation of the channel on next publish.
            _publishChannel = null;
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StartAsync(string topic, string subscription, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken ct)
    {
        var connection = await ConnectionAsync(ct);
        var ch = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        _consumerChannels.Add(ch);
        var queue = Queue(topic, subscription);
        await DeclareTopologyAsync(ch, Exchange(topic), queue, ct);
        await ch.BasicQosAsync(0, (ushort)Opt.PrefetchCount, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var envelope = ToEnvelope(ea);
            var attempt = ReadAttempt(ea.BasicProperties);
            try
            {
                await handler(envelope, ct);
                await ch.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex)
            {
                if (FailurePolicy.Decide(ex, attempt, Opt.MaxDeliveryAttempts) == FailureAction.DeadLetter)
                {
                    FnbMetrics.MessagesDeadLettered.Add(1);
                    logger.LogError(ex, "Message {MessageId} ({Type}) moved to DLQ {Queue} after {Attempt} attempts", envelope.MessageId, envelope.Type, queue + ".dlq", attempt);
                    var props = BuildProperties(envelope, attempt);
                    props.Headers!["x-error"] = Truncate(ex.Message, 500);
                    await ch.BasicPublishAsync("", queue + ".dlq", mandatory: false, props, ea.Body, ct);
                }
                else
                {
                    var delay = Opt.RetryDelay(attempt);
                    logger.LogWarning(ex, "Message {MessageId} failed on attempt {Attempt}; retrying in {Delay}", envelope.MessageId, attempt, delay);
                    await Task.Delay(delay, ct);
                    await ch.BasicPublishAsync("", queue, mandatory: false, BuildProperties(envelope, attempt + 1), ea.Body, ct);
                }

                // Ack only after the retry/DLQ copy has been confirmed by the broker.
                await ch.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            }
        };
        await ch.BasicConsumeAsync(queue, autoAck: false, consumer, ct);
        logger.LogInformation("RabbitMQ consumer started on {Queue}", queue);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        foreach (var ch in _consumerChannels)
        {
            await ch.CloseAsync(ct);
        }

        _consumerChannels.Clear();
    }

    private static BasicProperties BuildProperties(MessageEnvelope e, int attempt) => new()
    {
        Persistent = true,
        ContentType = "application/json",
        MessageId = e.MessageId.ToString(),
        CorrelationId = e.CorrelationId,
        Type = e.Type,
        Headers = new Dictionary<string, object?>
        {
            ["occurred-at-utc"] = e.OccurredAtUtc.ToString("O"),
            ["schema-version"] = e.SchemaVersion,
            [AttemptHeader] = attempt,
        },
    };

    private static int ReadAttempt(IReadOnlyBasicProperties props) =>
        props.Headers is not null && props.Headers.TryGetValue(AttemptHeader, out var v) && v is int i ? i : 1;

    private static MessageEnvelope ToEnvelope(BasicDeliverEventArgs ea)
    {
        var p = ea.BasicProperties;
        var occurred = p.Headers is not null && p.Headers.TryGetValue("occurred-at-utc", out var o) && o is byte[] ob
            && DateTime.TryParse(Encoding.UTF8.GetString(ob), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow;
        var schema = p.Headers is not null && p.Headers.TryGetValue("schema-version", out var s) && s is int si ? si : 1;
        return new MessageEnvelope(
            Guid.TryParse(p.MessageId, out var id) ? id : throw new PermanentMessageException("Missing MessageId"),
            p.Type ?? "",
            p.CorrelationId ?? "",
            occurred,
            schema,
            Encoding.UTF8.GetString(ea.Body.Span));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public async ValueTask DisposeAsync()
    {
        if (_publishChannel is not null)
        {
            await _publishChannel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
