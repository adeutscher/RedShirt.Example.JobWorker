using Pulsar.Client.Api;
using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

internal interface IPulsarConsumerWrapper : IAsyncDisposable, IDisposable
{
    Task AcknowledgeAsync(IPulsarMessageContainer message, CancellationToken cancellationToken = default);

    Task CommitAsync(IReadOnlyList<IPulsarMessageContainer> messages, CancellationToken cancellationToken = default);

    Task<IPulsarMessageContainer?> ConsumeAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    Task NegativeAcknowledgeAsync(IPulsarMessageContainer message, CancellationToken cancellationToken = default);

    Task NegativeAcknowledgeAsync(IReadOnlyList<IPulsarMessageContainer> messages,
        CancellationToken cancellationToken = default);
}

internal sealed class PulsarConsumerWrapper(
    IPulsarRetryWrapperService retryWrapper,
    PulsarClient? client,
    IConsumer<string> consumer,
    string topic) : IPulsarConsumerWrapper
{
    private bool _disposed;

    public async Task<IPulsarMessageContainer?> ConsumeAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var message = await consumer.ReceiveAsync(timeoutCts.Token);
            return MapMessage(message, topic);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out waiting for a message — treat as an empty poll, matching Kafka Consume(timeout).
            return null;
        }
    }

    public Task AcknowledgeAsync(IPulsarMessageContainer message, CancellationToken cancellationToken = default)
    {
        return CommitAsync([message], cancellationToken);
    }

    public async Task CommitAsync(IReadOnlyList<IPulsarMessageContainer> messages,
        CancellationToken cancellationToken = default)
    {
        /*
         * Shared subscriptions require individual acknowledgements (cumulative ack is not allowed).
         * Acknowledge each message independently so a failure on one does not prevent acknowledging others.
         */
        Exception? storedException = null;

        foreach (var message in messages)
        {
            if (message.PulsarMessageId is null)
            {
                continue;
            }

            try
            {
                await retryWrapper.RunAsync(async ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    await consumer.AcknowledgeAsync(message.PulsarMessageId);
                }, cancellationToken);
            }
            catch (WorkerJobSourceException e) when (e is {IsCritical: false})
            {
                storedException = e;
            }
        }

        if (storedException is not null)
        {
            throw storedException;
        }
    }

    public Task NegativeAcknowledgeAsync(IPulsarMessageContainer message,
        CancellationToken cancellationToken = default)
    {
        return NegativeAcknowledgeAsync([message], cancellationToken);
    }

    public async Task NegativeAcknowledgeAsync(IReadOnlyList<IPulsarMessageContainer> messages,
        CancellationToken cancellationToken = default)
    {
        /*
         * Negative acknowledgement schedules redelivery and increments the redelivery count toward
         * DeadLetterPolicy.MaxRedeliveryCount.
         */
        Exception? storedException = null;

        foreach (var message in messages)
        {
            if (message.PulsarMessageId is null)
            {
                continue;
            }

            try
            {
                await retryWrapper.RunAsync(async ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    await consumer.NegativeAcknowledge(message.PulsarMessageId);
                }, cancellationToken);
            }
            catch (WorkerJobSourceException e) when (e is {IsCritical: false})
            {
                storedException = e;
            }
        }

        if (storedException is not null)
        {
            throw storedException;
        }
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

    internal static IPulsarMessageContainer MapMessage(Message<string> message, string fallbackTopic)
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
}