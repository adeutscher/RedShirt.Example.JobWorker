using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal interface ISqsMessageSource
{
    Task<List<Message>> GetMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}

internal class SqsMessageSource(IAmazonSQS sqs, IOptions<SqsConfigurationModel> options) : ISqsMessageSource
{
    private const int MaxMessagesPerRequest = 10;

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

    public async Task<List<Message>> GetMessagesAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = new List<Message>();

        while (batchSize > MaxMessagesPerRequest)
        {
            var loopResult = await GetAsync(MaxMessagesPerRequest, cancellationToken);

            messages.AddRange(loopResult);

            if (loopResult.Count < MaxMessagesPerRequest)
            {
                // Received less than our batch size
                break;
            }

            batchSize -= MaxMessagesPerRequest;
        }

        if (batchSize is > 0 and <= MaxMessagesPerRequest)
        {
            messages.AddRange(await GetAsync(batchSize, cancellationToken));
        }

        return messages;
    }
}