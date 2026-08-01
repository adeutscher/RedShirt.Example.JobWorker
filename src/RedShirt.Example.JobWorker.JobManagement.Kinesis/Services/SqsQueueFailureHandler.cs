using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal class SqsQueueFailureHandler(IAmazonSQS sqs, IOptions<SqsQueueFailureHandler.ConfigurationModel> options)
    : IJobFailureHandler
{
    public Task HandleFailureAsync(IRawJobModel rawJobModel, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Value.QueueUrl))
        {
            return Task.CompletedTask;
        }

        return sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MessageBody = rawJobModel.Body
        }, cancellationToken);
    }

    internal class ConfigurationModel
    {
        public required string QueueUrl { get; init; }
    }
}