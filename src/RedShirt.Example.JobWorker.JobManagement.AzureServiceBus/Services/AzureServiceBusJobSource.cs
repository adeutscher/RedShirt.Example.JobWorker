using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal class AzureServiceBusJobSource(
    IAzureServiceBusClientRetryWrapper clientRetryWrapper,
    IAzureServiceBusMessageSource azureServiceBusServiceSource,
    IAzureServiceBusDetailedExceptionArbiter detailedExceptionArbiter,
    IOptions<AzureServiceBusConfigurationModel> options) : IJobSource
{
    private bool _nextConnectionAttemptShouldForceNewClient;

    public async Task AcknowledgeAsync(IRawJobModel message, CoreJobResult result,
        CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel)
        {
            return;
        }

        await clientRetryWrapper.GetClientAndDoActionWithRetryAsync(async (client, ct) =>
        {
            if (result.IsSuccessful())
            {
                await client.CompleteMessageAsync(messageAsAzureJobModel.Message, ct);
            }
            else if (result.IsRecoverableFailure())
            {
                if (options.Value.AbandonRecoveredFailuresOnAcknowledge)
                {
                    await client.AbandonMessageAsync(messageAsAzureJobModel.Message, ct);
                }
            }
            else
            {
                await client.DeadLetterMessageAsync(messageAsAzureJobModel.Message, result.ToString(),
                    cancellationToken: ct);
            }
        }, _nextConnectionAttemptShouldForceNewClient, cancellationToken);
        _nextConnectionAttemptShouldForceNewClient = false;
    }

    public async Task<IJobSourceResponse> GetJobsAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var messagesFromSource =
                await azureServiceBusServiceSource.GetMessagesAsync(batchSize, cancellationToken);
            var items = messagesFromSource
                .Select(receivedMessage => new AzureRawJobModel
                {
                    Message = receivedMessage,
                    CreatedAtUtc = DateTime.UtcNow
                } as IRawJobModel).ToList();

            return new JobSourceResponse
            {
                Items = items
            };
        }
        catch (WorkerJobSourceException e) when (e.IsPotentialCredentialProblem()
                                                 || detailedExceptionArbiter.IsReasonToReconnect(e))
        {
            _nextConnectionAttemptShouldForceNewClient = true;
            throw;
        }
    }

    public int RecommendedHeartbeatIntervalSeconds =>
        (int) Math.Ceiling(options.Value.EffectiveVisibilityTimeoutSeconds * 0.75);

    public bool IsSubscriptionSource => false;

    public async Task HeartbeatAsync(IRawJobModel message, CancellationToken cancellationToken = default)
    {
        if (message is not AzureRawJobModel messageAsAzureJobModel)
        {
            return;
        }

        await clientRetryWrapper.GetClientAndDoActionWithRetryAsync(
            async (client, ct) => { await client.RenewMessageLockAsync(messageAsAzureJobModel.Message, ct); },
            _nextConnectionAttemptShouldForceNewClient, cancellationToken);
        _nextConnectionAttemptShouldForceNewClient = false;
    }

    public Task StartSubscriberAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}