using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    ISourceMessageSorter sorter,
    ILogger<SqsJobSource> logger,
    IOptions<SqsConfigurationModel> options) : IJobSource
{
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
            Items = items.Count > 0 ? sorter.GetSortedListOfJobs(items) : []
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        return sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = message.MessageId,
            VisibilityTimeout = Math.Max(1, options.Value.VisibilityTimeoutSeconds)
        }, cancellationToken);
    }
}