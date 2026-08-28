using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

/// <summary>
///     Central point to perform operations on a Service Bus client and manage retries.
/// </summary>
internal interface IAzureServiceBusClientRetryWrapper
{
    Task GetClientAndDoActionWithRetryAsync(Func<IServiceBusClientWrapper, CancellationToken, Task> callback,
        bool forceNewClientImmediately = false,
        CancellationToken cancellationToken = default);

    void ResetClient();
}

internal class AzureServiceBusClientRetryWrapper(
    IAzureServiceBusRetryWrapperService retryWrapperService,
    IBusReceiverClientSource clientSource) : IAzureServiceBusClientRetryWrapper
{
    private IServiceBusClientWrapper? _mostRecentClient;

    private static LocalExceptionJudgement GetExceptionJudgement(Exception? exception, int retryNumber)
    {
        var regenerateClient = false;
        var forceNewSecretManagerPull = false;

        if (exception is not null
            && exception.IsPotentialCredentialProblem()
            && retryNumber == 1)
        {
            return new LocalExceptionJudgement
            {
                RegenerateClient = true,
                ForceNewSecretManagerPull = true
            };
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            regenerateClient = current switch
            {
                ServiceBusException
                {
                    Reason: ServiceBusFailureReason.ServiceTimeout
                    or ServiceBusFailureReason.ServiceBusy
                    or ServiceBusFailureReason.ServiceCommunicationProblem
                    or ServiceBusFailureReason.QuotaExceeded
                } or ObjectDisposedException => true,
                _ => regenerateClient
            };
        }

        if (exception is ServiceBusException {IsTransient: true})
        {
            regenerateClient = true;
        }

        return new LocalExceptionJudgement
        {
            RegenerateClient = regenerateClient,
            ForceNewSecretManagerPull = forceNewSecretManagerPull
        };
    }

    private async Task CallbackAsync(Func<IServiceBusClientWrapper, CancellationToken, Task> callback,
        ClientState state,
        bool immediatelyRefreshClient,
        CancellationToken cancellationToken)
    {
        if (state.Exception is not null)
        {
            state.RetryNumber++;
            _mostRecentClient = null;
        }

        var localExceptionJudgement = GetExceptionJudgement(state.Exception, state.RetryNumber);

        try
        {
            var clientWrapper = await clientSource.GetQueueClientAsync(
                immediatelyRefreshClient || localExceptionJudgement.RegenerateClient,
                localExceptionJudgement.ForceNewSecretManagerPull,
                cancellationToken);

            if (localExceptionJudgement.RegenerateClient || _mostRecentClient is null)
            {
                _mostRecentClient = clientWrapper.Client;
            }

            await callback(_mostRecentClient, cancellationToken);
        }
        catch (Exception e)
        {
            state.Exception = e;
            throw;
        }
    }

    public void ResetClient()
    {
        _mostRecentClient = null;
    }

    public Task GetClientAndDoActionWithRetryAsync(Func<IServiceBusClientWrapper, CancellationToken, Task> callback,
        bool forceNewClientImmediately = false,
        CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            (state, ct) => CallbackAsync(callback, state, forceNewClientImmediately, ct),
            new ClientState
            {
                Exception = null,
                RetryNumber = 0
            }, cancellationToken);
    }

    private sealed class LocalExceptionJudgement
    {
        public required bool RegenerateClient { get; init; }
        public required bool ForceNewSecretManagerPull { get; init; }
    }

    private sealed class ClientState
    {
        public required Exception? Exception { get; set; }
        public required int RetryNumber { get; set; }
    }
}