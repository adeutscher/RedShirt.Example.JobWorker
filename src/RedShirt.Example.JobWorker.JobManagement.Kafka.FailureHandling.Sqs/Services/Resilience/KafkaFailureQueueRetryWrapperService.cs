using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services.Resilience;

/// <summary>
///     Retries failure queue SQS operations, then surfaces classified failures as <see cref="WorkerJobSourceException" />,
///     the exception type that Core treats as an anticipated job source failure. This matches the Kinesis job source,
///     whose retry wrapper also surfaces <see cref="WorkerJobSourceException" /> for its failure queue.
/// </summary>
internal interface IKafkaFailureQueueRetryWrapperService
{
    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

internal sealed class KafkaFailureQueueRetryWrapperService(ISqsRetryWrapperService sqsRetryWrapperService)
    : IKafkaFailureQueueRetryWrapperService
{
    public async Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        try
        {
            await sqsRetryWrapperService.RunAsync(func, cancellationToken);
        }
        catch (WorkerSqsException e)
        {
            // Unclassified exceptions are not WorkerSqsException, so they stay raw and still draw attention.
            throw new WorkerJobSourceException(e)
            {
                IsHandled = e.IsHandled,
                CouldBeTransient = e.CouldBeTransient,
                CouldBeExternallySolvable = e.CouldBeExternallySolvable
            };
        }
    }
}
