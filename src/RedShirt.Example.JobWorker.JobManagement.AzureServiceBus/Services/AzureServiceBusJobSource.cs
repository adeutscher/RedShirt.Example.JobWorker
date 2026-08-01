using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal class AzureServiceBusJobSource(
    IBusReceiverClientSource clientSource,
    IAzureServiceBusMessageSource azureServiceBusServiceSource,
    IOptions<AzureServiceBusConfigurationModel> options) : IJobSource
{
    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);

        if (result.IsSuccessful())
        {
            await client.CompleteMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
        }
        else if (result.IsRecoverableFailure())
        {
            /*
             * Recoverable execution failures: abandon so the message becomes available again /
             * counts toward the service bus queue's configured maximum delivery count.
             */
            await client.AbandonMessageAsync(messageAsAzureJobModel.Message, cancellationToken);
        }
        else
        {
            // Empty / Parsing / Broken: dead-letter immediately.
            await client.DeadLetterMessageAsync(messageAsAzureJobModel.Message, result.ToString(),
                cancellationToken: cancellationToken);
        }
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messagesFromSource = await azureServiceBusServiceSource.GetMessagesAsync(batchSize, cancellationToken);
        var items = messagesFromSource
            .Select(receivedMessage => new AzureRawJobModel
            {
                Message = receivedMessage,
                CreatedAtUtc = DateTime.UtcNow
            } as IRawJobModel).ToList();

        var response = new JobSourceResponse
        {
            Items = items
        };

        return response;
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel)
            // For consideration: Throw some kind of exception?
        {
            return;
        }

        var client = await clientSource.GetQueueClientAsync(cancellationToken);
        await client.RenewMessageLockAsync(messageAsAzureJobModel.Message, cancellationToken);
    }
}