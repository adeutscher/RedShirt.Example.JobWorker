using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Models;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal class SqsJobSource(
    IAmazonSQS sqs,
    ISqsMessageSource sqsMessageSource,
    ISourceMessageConverter converter,
    ISqsPoisonMessagesHandler poisonMessagesHandler,
    ILogger<SqsJobSource> logger,
    IOptions<SqsConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (message is not SqsJobModel sqsJobModel)
        {
            // Message did not originate from this job source, ignore
            return;
        }

        if (!success)
        {
            await poisonMessagesHandler.AttemptPoisonMessageEnforcementAsync(sqsJobModel.RawMessage, cancellationToken);
            return;
        }

        await sqs.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = sqsJobModel.RawMessage.ReceiptHandle
        }, cancellationToken);
    }

    public async Task<JobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await sqsMessageSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = new List<IJobModel>();

        foreach (var message in messages)
        {
            try
            {
                logger.LogTrace("Raw SQS message: {MessageBody}", message.Body);

                var @object = converter.Convert(message.Body);
                if (@object is null)
                {
                    await poisonMessagesHandler.AttemptPoisonMessageEnforcementAsync(message, cancellationToken);

                    continue;
                }

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
                    Data = @object,
                    RawMessage = message
                };

                items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing SQS message: {MessageBody}", message.Body);

                await poisonMessagesHandler.AttemptPoisonMessageEnforcementAsync(message, cancellationToken);
            }
        }

        var response = new JobSourceResponse
        {
            Items = items
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
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

            throw new CanNoLongerHeartbeatException();
        }

        await sqs.ChangeMessageVisibilityAsync(request, cancellationToken);
    }
}