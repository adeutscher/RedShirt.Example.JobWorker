using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal class SqsQueueFailureHandler(
    IAmazonSQS sqs,
    IKinesisRetryWrapperService retryWrapperService,
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