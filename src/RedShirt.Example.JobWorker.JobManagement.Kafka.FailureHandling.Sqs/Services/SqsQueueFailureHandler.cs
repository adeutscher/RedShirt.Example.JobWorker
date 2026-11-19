using Amazon.SQS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services;

/// <summary>
///     Forwards failed Kafka records to an SQS failure queue.
///     <para>
///         A Kafka batch is only committed once every record in it has been acknowledged, and a failed record is only
///         acknowledged after this handler succeeds. To keep one record from stalling the consumer, a record that SQS
///         permanently rejects is forwarded as a reference without its body (see <see cref="SqsFailureMessageBuilder" />).
///         Transient failures are still thrown, so the record is not acknowledged.
///     </para>
/// </summary>
internal class SqsQueueFailureHandler(
    IAmazonSQS sqs,
    IKafkaFailureQueueRetryWrapperService retryWrapperService,
    IOptions<SqsQueueFailureHandler.ConfigurationModel> options,
    ILogger<SqsQueueFailureHandler> logger)
    : IJobFailureHandler
{
    public async Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        var queueUrl = options.Value.QueueUrl;
        if (string.IsNullOrEmpty(queueUrl))
        {
            return;
        }

        var request = SqsFailureMessageBuilder.Build(queueUrl, rawJobModel.MessageId, rawJobModel.Body);
        try
        {
            await retryWrapperService.RunAsync(ct => sqs.SendMessageAsync(request, ct), cancellationToken);
        }
        catch (Exception e) when (IsPermanentRejection(e) && !SqsFailureMessageBuilder.IsReference(request))
        {
            logger.LogWarning(e,
                "SQS rejected failed Kafka record {MessageId}. Forwarding a reference without the body.",
                rawJobModel.MessageId);
            var reference = SqsFailureMessageBuilder.BuildReference(queueUrl, rawJobModel.MessageId,
                $"SQS rejected the record body ({e.GetType().Name})");
            await retryWrapperService.RunAsync(ct => sqs.SendMessageAsync(reference, ct), cancellationToken);
        }
    }

    private static bool IsPermanentRejection(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => false,
            // Retries were exhausted on a transient cause: leave the record unacknowledged rather than drop its body.
            WorkerJobSourceException {CouldBeTransient: true} => false,
            _ => true
        };
    }

    internal class ConfigurationModel
    {
        public required string QueueUrl { get; init; }
    }
}
