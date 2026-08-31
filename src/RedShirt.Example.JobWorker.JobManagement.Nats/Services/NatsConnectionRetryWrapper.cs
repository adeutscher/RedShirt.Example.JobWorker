using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

/// <summary>
///     Central point to perform operations on a NATS JetStream consumer and manage connection retries.
/// </summary>
internal interface INatsConnectionRetryWrapper
{
    Task GetConsumerAndDoActionWithRetryAsync(Func<INatsJSConsumer, CancellationToken, Task> callback,
        bool forceNewConnectionImmediately = false,
        Action<INatsConnection>? onNewConnectionCallback = null,
        CancellationToken cancellationToken = default);

    void ResetConnection();
}

internal class NatsConnectionRetryWrapper(
    INatsRetryWrapperService retryWrapperService,
    INatsConnectionCacheSource connectionCacheSource,
    INatsConsumerSource consumerSource,
    INatsExceptionArbiterService exceptionArbiterService) : INatsConnectionRetryWrapper
{
    private LocalExceptionJudgement GetExceptionJudgement(Exception? exception, int retryNumber)
    {
        var regenerateConnection = false;
        var forceNewSecretManagerPull = false;

        if (exception is not null
            && exception.IsPotentialCredentialProblem()
            && exceptionArbiterService.GetReport(exception) is {CouldBeTransient: true}
            && retryNumber == 1)
        {
            return new LocalExceptionJudgement
            {
                RegenerateConnection = true,
                ForceNewSecretManagerPull = true
            };
        }

        // ReSharper disable once InvertIf
        if (exception is not null)
        {
            var report = exceptionArbiterService.GetReport(exception);
            if (report is {IsExpected: true, CouldBeTransient: true})
            {
                regenerateConnection = true;
            }
        }

        return new LocalExceptionJudgement
        {
            RegenerateConnection = regenerateConnection,
            ForceNewSecretManagerPull = forceNewSecretManagerPull
        };
    }

    private async Task CallbackAsync(Func<INatsJSConsumer, CancellationToken, Task> callback,
        RetryState state,
        Action<INatsConnection>? onNewConnectionCallback,
        bool immediatelyRefreshConnection,
        CancellationToken cancellationToken)
    {
        if (state.Exception is not null)
        {
            state.RetryNumber++;
            ResetConnection();
        }

        var localExceptionJudgement = GetExceptionJudgement(state.Exception, state.RetryNumber);
        var forceNewConnection = immediatelyRefreshConnection || localExceptionJudgement.RegenerateConnection;
        var forceNewSecretManagerPull = localExceptionJudgement.ForceNewSecretManagerPull;

        try
        {
            var connectionResponse = await connectionCacheSource.GetConnectionAsync(forceNewConnection,
                forceNewSecretManagerPull, cancellationToken);
            if (!connectionResponse.CachedClient)
            {
                onNewConnectionCallback?.Invoke(connectionResponse.Client.Connection);
            }

            var consumer = await consumerSource.GetConsumerAsync(forceNewConnection, forceNewSecretManagerPull,
                cancellationToken);
            await callback(consumer, cancellationToken);
        }
        catch (Exception e)
        {
            state.Exception = e;
            throw;
        }
    }

    public void ResetConnection()
    {
        consumerSource.ResetConsumer();
    }

    public Task GetConsumerAndDoActionWithRetryAsync(Func<INatsJSConsumer, CancellationToken, Task> callback,
        bool forceNewConnectionImmediately = false,
        Action<INatsConnection>? onNewConnectionCallback = null,
        CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            (state, ct) => CallbackAsync(callback, state, onNewConnectionCallback, forceNewConnectionImmediately, ct),
            new RetryState
            {
                Exception = null,
                RetryNumber = 0
            }, cancellationToken);
    }

    private sealed class LocalExceptionJudgement
    {
        public required bool RegenerateConnection { get; init; }
        public required bool ForceNewSecretManagerPull { get; init; }
    }

    private sealed class RetryState
    {
        public required Exception? Exception { get; set; }
        public required int RetryNumber { get; set; }
    }
}