using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

/// <summary>
///     Central point to perform operations on a Service Bus client/processor and manage retries.
/// </summary>
internal interface IAzureServiceBusClientRetryWrapper
{
    Task GetClientAndDoActionWithRetryAsync(Func<IServiceBusClientWrapper, CancellationToken, Task> callback,
        bool forceNewClientImmediately = false,
        CancellationToken cancellationToken = default);

    Task GetProcessorAndDoActionWithRetryAsync(Func<IServiceBusProcessorWrapper, CancellationToken, Task> callback,
        bool forceNewClientImmediately = false,
        Action<IServiceBusProcessorWrapper>? onNewProcessorCallback = null,
        CancellationToken cancellationToken = default);

    void ResetClient();

    void ResetProcessor();
}

internal class AzureServiceBusClientRetryWrapper(
    IAzureServiceBusRetryWrapperService retryWrapperService,
    IBusReceiverClientSource clientSource) : IAzureServiceBusClientRetryWrapper
{
    private IServiceBusClientWrapper? _mostRecentClient;
    private IServiceBusProcessorWrapper? _mostRecentProcessor;

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
            switch (current)
            {
                case ServiceBusException serviceBus when serviceBus.Reason is ServiceBusFailureReason.ServiceTimeout
                    or ServiceBusFailureReason.ServiceBusy
                    or ServiceBusFailureReason.ServiceCommunicationProblem
                    or ServiceBusFailureReason.QuotaExceeded:
                case ObjectDisposedException:
                    regenerateClient = true;
                    break;
            }
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

    private async Task ProcessorCallbackAsync(Func<IServiceBusProcessorWrapper, CancellationToken, Task> callback,
        ClientState state,
        Action<IServiceBusProcessorWrapper>? onNewProcessorCallback,
        bool immediatelyRefreshClient,
        CancellationToken cancellationToken)
    {
        if (state.Exception is not null)
        {
            state.RetryNumber++;
            _mostRecentProcessor = null;
        }

        var localExceptionJudgement = GetExceptionJudgement(state.Exception, state.RetryNumber);

        try
        {
            var processorWrapper = await clientSource.GetProcessorAsync(
                immediatelyRefreshClient || localExceptionJudgement.RegenerateClient,
                localExceptionJudgement.ForceNewSecretManagerPull,
                cancellationToken);

            if (!processorWrapper.CachedClient)
            {
                onNewProcessorCallback?.Invoke(processorWrapper.Client);
            }

            if (localExceptionJudgement.RegenerateClient || _mostRecentProcessor is null)
            {
                _mostRecentProcessor = processorWrapper.Client;
            }

            await callback(_mostRecentProcessor, cancellationToken);
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

    public void ResetProcessor()
    {
        _mostRecentProcessor = null;
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

    public Task GetProcessorAndDoActionWithRetryAsync(Func<IServiceBusProcessorWrapper, CancellationToken, Task> callback,
        bool forceNewClientImmediately = false,
        Action<IServiceBusProcessorWrapper>? onNewProcessorCallback = null,
        CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            (state, ct) => ProcessorCallbackAsync(callback, state, onNewProcessorCallback, forceNewClientImmediately,
                ct),
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
