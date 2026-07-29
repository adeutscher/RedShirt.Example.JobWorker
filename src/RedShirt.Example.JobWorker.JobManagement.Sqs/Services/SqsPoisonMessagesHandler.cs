using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal interface ISqsPoisonMessagesHandler
{
    Task AttemptPoisonMessageEnforcementAsync(Message message,
        CancellationToken cancellationToken = default);
}

internal class SqsPoisonMessagesHandler(IAmazonSQS sqs, IOptions<SqsConfigurationModel> options)
    : ISqsPoisonMessagesHandler
{
    public async Task AttemptPoisonMessageEnforcementAsync(Message message,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.DlqNotEnabled)
        {
            // If the DLQ is enabled, then leave poison message handling to the configuration of the SQS queue
            return;
        }

        if ((SqsMessageAttributeRetriever.TryGetApproximateReceiveCount(message) ?? 0) >=
            options.Value.EffectiveMaximumReceives)
        {
            // If the DLQ is not enabled, then attempt to deal with poison messages
            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = options.Value.QueueUrl,
                ReceiptHandle = message.ReceiptHandle
            }, cancellationToken);
        }
    }
}