using Confluent.Kafka;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Utility;

internal interface IKafkaConsumerWrapper : IDisposable
{
    Task CommitAsync(IReadOnlyList<IKafkaMessageContainer> messages, CancellationToken cancellationToken = default);
    IKafkaMessageContainer? Consume(TimeSpan timeout);
}

internal sealed class KafkaConsumerWrapper(IKafkaRetryWrapperService retryWrapper, IConsumer<string, string> consumer)
    : IKafkaConsumerWrapper
{
    private bool _disposed;

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            consumer.Close();
            consumer.Dispose();
        }

        _disposed = true;
    }

    public IKafkaMessageContainer? Consume(TimeSpan timeout)
    {
        var result = consumer.Consume(timeout);
        if (result?.Message is null)
        {
            return null;
        }

        return new KafkaMessageContainer
        {
            Result = result
        };
    }

    public async Task CommitAsync(IReadOnlyList<IKafkaMessageContainer> messages,
        CancellationToken cancellationToken = default)
    {
        var offsets = messages
            .Select(m => new TopicPartitionOffset(m.Topic, m.Partition, new Offset(m.Offset + 1)))
            .ToList();

        if (offsets.Count == 0)
        {
            return;
        }

        var problematicPartitionIds = new HashSet<int>();
        Exception? storedException = null;

        // Iterate through offsets one entry at a time to try to ensure that we commit as much as we are allowed to by Kafka.

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var offset in offsets)
        {
            if (problematicPartitionIds.Contains(offset.Partition))
            {
                // Previously encountered a non-transient problem with this particular partition in this invocation
                continue;
            }

            try
            {
                // Running retry wrapper on an individual basis so that problems on one partition don't bog down a working one
                await retryWrapper.RunAsync(_ =>
                {
                    consumer.Commit([offset]);
                    return Task.CompletedTask;
                }, cancellationToken);
            }
            catch (WorkerJobSourceException e)
            {
                // Suppressing non-critical exceptions, temporarily

                // Add to watch list to avoid trying to commit another message for this partition in this loop 
                problematicPartitionIds.Add(offset.Partition);

                // Store the exception to call after we're done looping
                // If an exception overwrites another, then assuming that no significant data was lost
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
        Dispose(true);
        // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
        GC.SuppressFinalize(this);
    }
}