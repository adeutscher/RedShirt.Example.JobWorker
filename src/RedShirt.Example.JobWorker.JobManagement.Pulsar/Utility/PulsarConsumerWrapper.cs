using Pulsar.Client.Api;
using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

internal interface IPulsarConsumerWrapper : IAsyncDisposable, IDisposable
{
    Task AcknowledgeAsync(IPulsarMessageContainer message, CancellationToken cancellationToken = default);

    Task<IPulsarMessageContainer?> ConsumeAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    Task NegativeAcknowledgeAsync(IPulsarMessageContainer message, CancellationToken cancellationToken = default);
}

internal sealed class PulsarConsumerWrapper(
    IPulsarRetryWrapperService retryWrapper,
    PulsarClient? client,
    IConsumer<string> consumer,
    string topic) : IPulsarConsumerWrapper
{
    private bool _disposed;

    private static PulsarMessageContainer MapMessage(Message<string> message, string fallbackTopic)
    {
        var topic = string.IsNullOrEmpty(message.MessageId.TopicName) ? fallbackTopic : message.MessageId.TopicName;
        return new PulsarMessageContainer
        {
            PulsarMessageId = message.MessageId,
            Key = message.Key,
            Value = message.GetValue(),
            Topic = topic
        };
    }

    public async Task<IPulsarMessageContainer?> ConsumeAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            // Confirming that at present we have no avenue other than a CancellationToken timeout to constrain the time that ReceiveAsync takes.
            var message = await consumer.ReceiveAsync(timeoutCts.Token);
            return MapMessage(message, topic);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out waiting for a message — treat as an empty poll, matching Kafka Consume(timeout).
            return null;
        }
    }

    public async Task AcknowledgeAsync(IPulsarMessageContainer message,
        CancellationToken cancellationToken = default)
    {
        if (message.PulsarMessageId is null)
        {
            return;
        }

        await retryWrapper.RunAsync(async ct =>
        {
            ct.ThrowIfCancellationRequested();
            await consumer.AcknowledgeAsync(message.PulsarMessageId);
        }, cancellationToken);
    }

    public async Task NegativeAcknowledgeAsync(IPulsarMessageContainer message,
        CancellationToken cancellationToken = default)
    {
        /*
         * Negative acknowledgement schedules redelivery and increments the redelivery count toward
         * DeadLetterPolicy.MaxRedeliveryCount.
         */
        if (message.PulsarMessageId is null)
        {
            return;
        }

        await retryWrapper.RunAsync(async ct =>
        {
            ct.ThrowIfCancellationRequested();
            await consumer.NegativeAcknowledge(message.PulsarMessageId);
        }, cancellationToken);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await consumer.DisposeAsync();
        if (client is not null)
        {
            await client.CloseAsync();
        }
    }
}