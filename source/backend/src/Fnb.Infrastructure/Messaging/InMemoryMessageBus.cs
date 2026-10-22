using System.Collections.Concurrent;
using System.Threading.Channels;
using Fnb.Application.Abstractions;
using Fnb.Contracts;
using Fnb.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fnb.Infrastructure.Messaging;

public enum FailureAction
{
    Retry,
    DeadLetter,
}

public static class FailurePolicy
{
    /// <summary>Permanent errors (bad payload, unrecoverable business errors) go straight to dead-letter; others retry up to the limit.</summary>
    public static FailureAction Decide(Exception ex, int attempt, int maxAttempts) =>
        ex is PermanentMessageException or System.Text.Json.JsonException || attempt >= maxAttempts
            ? FailureAction.DeadLetter
            : FailureAction.Retry;
}

/// <summary>
/// Development-only in-process broker (non-durable). Same semantics as the real adapters: at-least-once,
/// bounded retries with exponential backoff, dead-letter list. Use RabbitMQ/Azure Service Bus anywhere durability matters.
/// </summary>
public sealed class InMemoryMessageBus(IOptions<MessagingOptions> options, ILogger<InMemoryMessageBus> logger) : IMessagePublisher, IMessageSubscriber, IDisposable
{
    private sealed record Delivery(MessageEnvelope Envelope, int Attempt);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Channel<Delivery>>> _topics = new();
    private readonly List<Task> _loops = [];
    private readonly CancellationTokenSource _cts = new();

    public ConcurrentQueue<(string Subscription, MessageEnvelope Envelope, string Error)> DeadLetters { get; } = new();

    private Channel<Delivery> GetChannel(string topic, string subscription) =>
        _topics.GetOrAdd(topic, _ => new()).GetOrAdd(subscription, _ => Channel.CreateUnbounded<Delivery>());

    public Task PublishAsync(string topic, MessageEnvelope envelope, CancellationToken ct)
    {
        // Ensure well-known subscriptions exist so messages published before a consumer starts are not lost.
        if (topic == Topics.OrderEvents)
        {
            GetChannel(topic, Subscriptions.OrderProcessing);
            GetChannel(topic, Subscriptions.Realtime);
        }

        foreach (var channel in _topics.GetOrAdd(topic, _ => new()).Values)
        {
            channel.Writer.TryWrite(new Delivery(envelope, 1));
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(string topic, string subscription, Func<MessageEnvelope, CancellationToken, Task> handler, CancellationToken ct)
    {
        var channel = GetChannel(topic, subscription);
        var opt = options.Value;
        _loops.Add(Task.Run(async () =>
        {
            await foreach (var delivery in channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await handler(delivery.Envelope, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (FailurePolicy.Decide(ex, delivery.Attempt, opt.MaxDeliveryAttempts) == FailureAction.DeadLetter)
                    {
                        FnbMetrics.MessagesDeadLettered.Add(1);
                        DeadLetters.Enqueue((subscription, delivery.Envelope, ex.Message));
                        logger.LogError(ex, "Message {MessageId} ({Type}) dead-lettered on {Subscription} after {Attempt} attempts",
                            delivery.Envelope.MessageId, delivery.Envelope.Type, subscription, delivery.Attempt);
                    }
                    else
                    {
                        var delay = opt.RetryDelay(delivery.Attempt);
                        logger.LogWarning(ex, "Message {MessageId} failed (attempt {Attempt}); retrying in {Delay}", delivery.Envelope.MessageId, delivery.Attempt, delay);
                        _ = Task.Delay(delay, _cts.Token).ContinueWith(
                            _ => channel.Writer.TryWrite(delivery with { Attempt = delivery.Attempt + 1 }),
                            TaskScheduler.Default);
                    }
                }
            }
        }, CancellationToken.None));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _cts.CancelAsync();
        try
        {
            await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // shutting down
        }
    }

    public void Dispose() => _cts.Dispose();
}
