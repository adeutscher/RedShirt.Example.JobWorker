using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal class AzureServiceBusJobSource(
    IBusReceiverClientSource clientSource,
    IAzureServiceBusMessageSource azureServiceBusServiceSource,
    IAzureServiceBusRetryWrapperService retryWrapperService,
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

        await retryWrapperService.RunAsync(async ct =>
        {
            if (result.IsSuccessful())
            {
                await client.CompleteMessageAsync(messageAsAzureJobModel.Message, ct);
            }
            else if (result.IsRecoverableFailure())
            {
                if (options.Value.AbandonRecoveredFailuresOnAcknowledge)
                {
                    /*
                     * Recoverable execution failures: explicitly abandon (if configured) so the message becomes available again.
                     * If not abandoned, then the message should fall back into the queue within a minute.
                     * Either option counts toward the service bus queue's configured maximum delivery count.
                     */
                    await client.AbandonMessageAsync(messageAsAzureJobModel.Message, ct);
                }
            }
            else
            {
                // Empty / Parsing / InvalidData: dead-letter immediately.
                // One could argue that there's no point in even bothering to dead-letter Empty problems,
                //  but on the other hand there could be useful properties for debugging on the message.
                // This application's priority is just getting unrecoverable messages out of the way ASAP.
                await client.DeadLetterMessageAsync(messageAsAzureJobModel.Message, result.ToString(),
                    cancellationToken: ct);
            }
        }, cancellationToken);
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
        await retryWrapperService.RunAsync(
            async ct => { await client.RenewMessageLockAsync(messageAsAzureJobModel.Message, ct); }, cancellationToken);
    }
}