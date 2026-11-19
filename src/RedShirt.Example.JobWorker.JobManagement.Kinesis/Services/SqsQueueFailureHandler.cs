using Amazon.SQS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

/// <summary>
///     Forwards failed Kinesis records to an SQS failure queue.
///     <para>
///         A shard's checkpoint only advances once every record in its batch has been acknowledged, and a failed record
///         is only acknowledged after this handler succeeds. To keep one record from holding the shard, a record that SQS
///         permanently rejects is forwarded as a reference without its body (see <see cref="SqsFailureMessageBuilder" />).
///         Transient failures are still thrown, so the record is not acknowledged.
///     </para>
/// </summary>
internal class SqsQueueFailureHandler(
    IAmazonSQS sqs,
    IKinesisRetryWrapperService retryWrapperService,
    IOptions<SqsQueueFailureHandler.ConfigurationModel> options,
    ILogger<SqsQueueFailureHandler> logger)
    : IJobFailureHandler
{
    internal const string ShardIdAttribute = "SourceShardId";

    private static bool IsPermanentRejection(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => false,
            // Retries were exhausted on a transient cause: leave the record unacknowledged rather than drop its body.
            WorkerJobSourceException {CouldBeTransient: true} => false,
            WorkerSqsException {CouldBeTransient: true} => false,
            _ => true
        };
    }

    public async Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        var queueUrl = options.Value.QueueUrl;
        if (string.IsNullOrEmpty(queueUrl))
        {
            return;
        }

        // A sequence number is only unique within its shard, so carry the shard ID with it.
        var sourceAttributes = rawJobModel is KinesisJobModel kinesisJobModel
            ? new Dictionary<string, string> {[ShardIdAttribute] = kinesisJobModel.ShardId}
            : null;

        var request = SqsFailureMessageBuilder.Build(queueUrl, rawJobModel.MessageId, rawJobModel.Body,
            sourceAttributes);
        try
        {
            await retryWrapperService.RunAsync(ct => sqs.SendMessageAsync(request, ct), cancellationToken);
        }
        catch (Exception e) when (IsPermanentRejection(e) && !SqsFailureMessageBuilder.IsReference(request))
        {
            logger.LogWarning(e,
                "SQS rejected failed Kinesis record {MessageId}. Forwarding a reference without the body.",
                rawJobModel.MessageId);
            var reference = SqsFailureMessageBuilder.BuildReference(queueUrl, rawJobModel.MessageId,
                $"SQS rejected the record body ({e.GetType().Name})", sourceAttributes);
            await retryWrapperService.RunAsync(ct => sqs.SendMessageAsync(reference, ct), cancellationToken);
        }
    }

    internal class ConfigurationModel
    {
        public required string QueueUrl { get; init; }
    }
}