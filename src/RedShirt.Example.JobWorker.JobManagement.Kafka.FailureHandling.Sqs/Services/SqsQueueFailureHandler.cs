using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services;

internal class SqsQueueFailureHandler(
    IAmazonSQS sqs,
    ISqsRetryWrapperService retryWrapperService,
    IOptions<SqsQueueFailureHandler.ConfigurationModel> options)
    : IJobFailureHandler
{
    public Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Value.QueueUrl))
        {
            return Task.CompletedTask;
        }

        return retryWrapperService.RunAsync(ct => sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MessageBody = rawJobModel.Body
        }, ct), cancellationToken);
    }

    internal class ConfigurationModel
    {
        public required string QueueUrl { get; init; }
    }
}