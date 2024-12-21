using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Services;

internal class SqsJobSource(
    IAmazonSQS sqs,
    ISourceMessageConverter converter,
    ILogger<SqsJobSource> logger,
    IOptions<SqsJobSource.ConfigurationModel> options) : IJobSource
{
    private int VisibilityTimeoutSeconds => Math.Max(20, options.Value.VisibilityTimeoutSeconds);

    public Task AcknowledgeCompletionAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        return sqs.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = message.MessageId
        }, cancellationToken);
    }

    public async Task<JobSourceResponse> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var sqsResponse = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MaxNumberOfMessages = options.Value.MessageBatchSize,
            VisibilityTimeout = VisibilityTimeoutSeconds
        }, cancellationToken);

        var response = new JobSourceResponse
        {
            RecommendedHeartbeatIntervalSeconds = (int) Math.Ceiling(VisibilityTimeoutSeconds * 0.75),
            Items = []
        };

        foreach (var message in sqsResponse.Messages)
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

                response.Items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Raw SQS message: {MessageBody}", message.Body);
            }
        }

        return response;
    }

    public Task HeartbeatAsync(IJobModel message, CancellationToken cancellationToken = default)
    {
        return sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = options.Value.QueueUrl,
            ReceiptHandle = message.MessageId,
            VisibilityTimeout = Math.Max(1, options.Value.VisibilityTimeoutSeconds)
        }, cancellationToken);
    }

    public class ConfigurationModel
    {
        public required string QueueUrl { get; init; }
        public required int MessageBatchSize { get; init; }
        public required int VisibilityTimeoutSeconds { get; init; }
    }
}