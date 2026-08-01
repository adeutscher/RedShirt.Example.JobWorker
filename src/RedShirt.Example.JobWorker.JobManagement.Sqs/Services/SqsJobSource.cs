using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Enums;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Models;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal class SqsJobSource(
    IAmazonSQS sqs,
    ISqsMessageSource sqsMessageSource,
    ISqsPoisonMessagesHandler poisonMessagesHandler,
    IOptions<SqsConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not SqsJobModel sqsJobModel)
        {
            // Message did not originate from this job source, ignore
            return;
        }

        if (result.IsSuccessful())
        {
            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                ReceiptHandle = sqsJobModel.RawMessage.ReceiptHandle
            }, cancellationToken);
            return;
        }

        // If we have reached here, then the result was not successful.

        // Whether the failed message was recoverable or not,
        //  the SQS implementation's reaction is to attempt to enforce
        //  a consumer-based poison-handling system    
        var poisonEnforcementResult =
            await poisonMessagesHandler.AttemptPoisonMessageEnforcementAsync(sqsJobModel.RawMessage, cancellationToken);

        if (poisonEnforcementResult != PoisonEnforcement.Enforced
            && !result.IsRecoverableFailure())
        {
            // The message was not already deleted by consumer-based poison message handling and the message is not recoverable, so the message should be deleted.
            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                ReceiptHandle = sqsJobModel.RawMessage.ReceiptHandle
            }, cancellationToken);
        }
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await sqsMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IRawJobModel>();

        foreach (var message in messages)
        {
            var data = new SqsJobModel
            {
                MessageId = message.MessageId,
                /*
                 * Documentation and AI summaries for SQS emphasize that the message's 12-hour in-flight limit
                 * is marked based off of the "*first* receive", as indicated by the ApproximateFirstReceiveUtc property.
                 *
                 * I think that this is weird and counter-productive.
                 * So Amazon is saying that if a message is first received and then processes for a decent amount of time before failing and then falling back into the queue that subsequent receives have even less time?
                 * If that is how SQS is designed then so be it, but it just feels like an unnecessary extra reason to consider an entirely different message broker than SQS for workloads that legitimately run long.
                 * I don't know, I just wanted to get my grievances out somewhere.
                 */
                CreatedAtUtc = SqsMessageAttributeRetriever.TryGetApproximateFirstReceiveUtc(message) ??
                               DateTime.UtcNow,
                Body = message.Body,
                RawMessage = message
            };

            items.Add(data);
        }

        var response = new JobSourceResponse
        {
            Items = items
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not SqsJobModel sqsJobModel)
        {
            // Message did not originate from this job source, ignore
            return;
        }

        var request = new ChangeMessageVisibilityRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = sqsJobModel.RawMessage.ReceiptHandle,
            VisibilityTimeout = options.Value.EffectiveVisibilityTimeoutSeconds
        };

        if (DateTime.UtcNow + TimeSpan.FromSeconds(request.VisibilityTimeout.Value) >
            message.CreatedAtUtc + TimeSpan.FromSeconds(SqsConfigurationModel.MaximumVisibilityTimeoutAmountSeconds))
        {
            /*
             * If we're about to be no longer able to extend the in-flight time of this message, then delete the message.
             * To confirm, SQS has a built-in hard limit wherein a message cannot be kept in flight for more than 12 hours.
             *
             * This is far from an ideal solution, but it keeps the queue from being overwhelmed by a long-running job multiple times.
             *
             * If this 12-hour job isn't an outlier, then you should strongly consider using a message broker other than SQS or breaking up the workload into smaller chunks.
             *
             * AWSSDK does not return an exception when we try to extend beyond the 12-hour limit, so we are forced to apply our own.
             */

            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                ReceiptHandle = sqsJobModel.RawMessage.ReceiptHandle
            }, cancellationToken);

            throw new WorkerJobSourceException(
                "Message is in danger of exceeding maximum SQS visibility timeout.",
                false);
        }

        await sqs.ChangeMessageVisibilityAsync(request, cancellationToken);
    }
}