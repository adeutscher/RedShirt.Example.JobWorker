using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal interface ISqsMessageSource
{
    Task<List<Message>> GetMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}

internal class SqsMessageSource(
    IAmazonSQS sqs,
    ISqsJobSourceRetryWrapperService retryWrapperService,
    IOptions<SqsConfigurationModel> options) : ISqsMessageSource
{
    private const int MaxMessagesPerRequest = 10;

    private Task<List<Message>> GetAsync(int batchSize, bool useWaitTime, CancellationToken cancellationToken)
    {
        /*
         * Deliberately short-polling for messages.
         * Long-polling could technically return more messages.
         * But on the other hand, it could cause delays in processing the messages that we have actually received.
         */
        return retryWrapperService.RunAsync(async ct =>
        {
            var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                MaxNumberOfMessages = batchSize,
                VisibilityTimeout = options.Value.EffectiveVisibilityTimeoutSeconds,
                MessageSystemAttributeNames =
                [
                    SqsConstants.AttributeApproximateFirstReceiveTimestamp,
                    SqsConstants.AttributeApproximateReceiveCount
                ],
                WaitTimeSeconds = useWaitTime && options.Value.EffectiveWaitTimeSeconds > 0
                    ? options.Value.EffectiveWaitTimeSeconds
                    : null
            }, ct);

            return response?.Messages ?? [];
        }, cancellationToken);
    }

    public async Task<List<Message>> GetMessagesAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = new List<Message>();
        var firstRequest = true;

        while (batchSize > MaxMessagesPerRequest)
        {
            var loopResult = await GetAsync(MaxMessagesPerRequest, firstRequest, cancellationToken);
            firstRequest = false;

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
            messages.AddRange(await GetAsync(batchSize, firstRequest, cancellationToken));
        }

        return messages;
    }
}