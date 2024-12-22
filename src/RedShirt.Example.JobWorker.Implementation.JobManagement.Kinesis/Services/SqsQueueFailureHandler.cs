using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal class SqsQueueFailureHandler(IAmazonSQS sqs, IOptions<SqsQueueFailureHandler.ConfigurationModel> options)
    : IJobFailureHandler
{
    public Task HandleFailureAsync(IJobModel jobModel, Exception exception,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Value.QueueUrl))
        {
            return Task.CompletedTask;
        }

        return sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MessageBody = JsonSerializer.Serialize(jobModel.Data)
        }, cancellationToken);
    }

    public class ConfigurationModel
    {
        public required string QueueUrl { get; init; }
    }
}