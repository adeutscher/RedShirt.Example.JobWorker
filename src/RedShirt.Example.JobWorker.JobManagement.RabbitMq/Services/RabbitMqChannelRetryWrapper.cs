using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Central point to perform operations on a channel and manage retries.
/// </summary>
internal interface IRabbitMqChannelRetryWrapper
{
    Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        Action<IConnection>? onNewConnectionCallback = null, CancellationToken cancellationToken = default);

    void ResetChannel();
}

internal class RabbitMqChannelRetryWrapper(
    IRabbitMqRetryWrapperService retryWrapperService,
    IRabbitMqConnectionCacheSource connectionCacheSource,
    IRabbitMqExceptionArbiterService exceptionArbiterService) : IRabbitMqChannelRetryWrapper
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IChannel? _mostRecentChannel;

    /// <summary>
    ///     Local method to make decisions on what needs regenerating, with the goal of making <see cref="CallbackAsync" /> a
    ///     bit more readable.
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    private LocalExceptionJudgement GetExceptionJudgement(ChannelState state)
    {
        // Using previous iteration's exception stored in state to judge whether we need to regenerate the connection and/or channel.
        var regenerateConnection = false;
        var regenerateChannel = false;
        var forceNewSecretManagerPull = false;

        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (state.Exception is AuthenticationFailureException or PossibleAuthenticationFailureException
            && exceptionArbiterService.GetReport(state.Exception, state.RetryNumber) is {CouldBeTransient: true})
        {
            forceNewSecretManagerPull = true;
            regenerateConnection = true;
            regenerateChannel = true;
        }

        if (state.Exception is OperationInterruptedException
            {
                ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ConnectionCodeRangeAMin
                and <= RabbitMqExceptionCodeConstants.ConnectionCodeRangeAMax
            }
            or OperationInterruptedException
            {
                ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ConnectionCodeRangeBMin
                and <= RabbitMqExceptionCodeConstants.ConnectionCodeRangeBMax
            })
        {
            regenerateConnection = true;
        }

        if (regenerateConnection || state.Exception is OperationInterruptedException
            {
                ShutdownReason.ReplyCode: >= RabbitMqExceptionCodeConstants.ChannelCodeMin
                and <= RabbitMqExceptionCodeConstants.ChannelCodeMax
            })
        {
            regenerateChannel = true;
        }

        return new LocalExceptionJudgement
        {
            RegenerateConnection = regenerateConnection,
            RegenerateChannel = regenerateChannel,
            ForceNewSecretManagerPull = forceNewSecretManagerPull
        };
    }

    private async Task CallbackAsync(Func<IChannel, CancellationToken, Task> callback,
        ChannelState state,
        Action<IConnection>? onNewConnectionCallback,
        CancellationToken cancellationToken)
    {
        if (state.Exception is not null)
        {
            state.RetryNumber++;
            ResetChannel();
        }

        var localExceptionJudgement = GetExceptionJudgement(state);

        try
        {
            IConnection? connection;
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                var connectionWrapper = await connectionCacheSource.GetConnectionAsync(
                    localExceptionJudgement.RegenerateConnection,
                    localExceptionJudgement.ForceNewSecretManagerPull,
                    cancellationToken);
                if (!connectionWrapper.CachedConnection)
                {
                    // Fresh connection
                    onNewConnectionCallback?.Invoke(connectionWrapper.Connection);
                }

                connection = connectionWrapper.Connection;
            }
            finally
            {
                _connectionLock.Release();
            }

            if (localExceptionJudgement.RegenerateChannel || _mostRecentChannel is null)
            {
                _mostRecentChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            }

            await callback(_mostRecentChannel!, cancellationToken);
        }
        catch (Exception e)
        {
            state.Exception = e;
            throw;
        }
    }

    public void ResetChannel()
    {
        _mostRecentChannel = null;
    }

    public Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        Action<IConnection>? onNewConnectionCallback = null, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            (state, ct) => CallbackAsync(callback, state, onNewConnectionCallback, ct),
            new ChannelState
            {
                Exception = null,
                RetryNumber = 0
            }, cancellationToken);
    }

    private sealed class LocalExceptionJudgement
    {
        public required bool RegenerateConnection { get; init; }
        public required bool RegenerateChannel { get; init; }
        public required bool ForceNewSecretManagerPull { get; init; }
    }

    private sealed class ChannelState
    {
        public required Exception? Exception { get; set; }
        public required int RetryNumber { get; set; }
    }
}