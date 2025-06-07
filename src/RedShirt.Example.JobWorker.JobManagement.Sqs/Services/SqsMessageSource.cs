using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal interface ISqsMessageSource
{
    Task<List<Message>> GetMessagesAsync(CancellationToken cancellationToken = default);
}

internal class SqsMessageSource(IAmazonSQS sqs, IOptions<SqsConfigurationModel> options) : ISqsMessageSource
{
    private async Task<List<Message>> GetAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MaxNumberOfMessages = batchSize,
            VisibilityTimeout = options.Value.EffectiveVisibilityTimeoutSeconds
        }, cancellationToken);

        return response?.Messages ?? [];
    }

    public async Task<List<Message>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        var messages = new List<Message>();
        var batchSize = options.Value.MessageBatchSize;

        while (batchSize > 10)
        {
            var loopResult = await GetAsync(10, cancellationToken);

            messages.AddRange(loopResult);

            if (loopResult.Count < 10)
            {
                // Received less than our batch size
                break;
            }

            batchSize -= 10;
        }

        if (batchSize > 0 && batchSize <= 10)
        {
            messages.AddRange(await GetAsync(batchSize, cancellationToken));
        }

        return messages;
    }
}