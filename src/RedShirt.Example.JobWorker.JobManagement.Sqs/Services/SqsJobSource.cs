using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal class SqsJobSource(
    IAmazonSQS sqs,
    ISqsMessageSource sqsMessageSource,
    ISourceMessageConverter converter,
    ILogger<SqsJobSource> logger,
    IOptions<SqsConfigurationModel> options) : IJobSource
{
    private const int MaximumInFlightTimeHours = 12;

    public Task AcknowledgeCompletionAsync(IJobModel message, bool success,
        CancellationToken cancellationToken = default)
    {
        if (!success)
        {
            return Task.CompletedTask;
        }

        return sqs.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = message.MessageId
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
                    continue;
                }

                var data = new JobModel
                {
                    MessageId = message.ReceiptHandle,
                    CreatedAtUtc = DateTime.UtcNow,
                    Data = @object
                };

                items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing SQS message: {MessageBody}", message.Body);
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
        var request = new ChangeMessageVisibilityRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = message.MessageId,
            VisibilityTimeout = options.Value.EffectiveVisibilityTimeoutSeconds
        };

        if (DateTime.UtcNow + TimeSpan.FromSeconds(request.VisibilityTimeout.Value) >
            message.CreatedAtUtc + TimeSpan.FromHours(MaximumInFlightTimeHours))
        {
            /*
             * If we're about to be no longer able to extend the in-flight time of this message,
             * then delete the message.
             *
             * This isn't exactly an ideal solution,
             * but it keeps the queue from being overwhelmed by a long-running job multiple times.
             *
             * If this 12-hour job isn't an outlier, then you should consider using a job source other than SQS
             * or breaking up the workload into smaller chunks.
             * 
             * AWSSDK does not return an exception when we try to extend beyond the 12 hour limit,
             * so we are forced to apply our own. 
             */

            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                ReceiptHandle = message.MessageId
            }, cancellationToken);

            throw new CanNoLongerHeartbeatException();
        }

        await sqs.ChangeMessageVisibilityAsync(request, cancellationToken);
    }
}