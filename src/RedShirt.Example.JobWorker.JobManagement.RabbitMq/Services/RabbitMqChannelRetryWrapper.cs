using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

internal interface IRabbitMqChannelRetryWrapper
{
    Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        Action<IConnection>? onNewConnectionCallback = null, CancellationToken cancellationToken = default);
}

internal class RabbitMqChannelRetryWrapper(
    IRabbitMqRetryWrapperService retryWrapperService,
    IRabbitMqConnectionCacheSource connectionCacheSource) : IRabbitMqChannelRetryWrapper
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IChannel? _mostRecentChannel;

    public Task GetChannelAndDoActionWithRetryAsync(Func<IChannel, CancellationToken, Task> callback,
        Action<IConnection>? onNewConnectionCallback = null, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(async (state, ct) =>
        {
            // Using previous iteration's exception stored in state to judge whether we need to regenerate the connection and/or channel.
            var regenerateConnection = false;
            var regenerateChannel = false;

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

            try
            {
                IConnection? connection;
                await _connectionLock.WaitAsync(ct);
                try
                {
                    var connectionWrapper = await connectionCacheSource.GetConnectionAsync(regenerateConnection, ct);
                    if (!connectionWrapper.CachedConnection)
                    {
                        // Fresh connection

                        // Confirm that we aren't doubling-up on RecoverySucceededAsync
                        onNewConnectionCallback?.Invoke(connectionWrapper.Connection);

                        regenerateChannel = true;
                    }

                    connection = connectionWrapper.Connection;
                }
                finally
                {
                    _connectionLock.Release();
                }

                if (regenerateChannel)
                {
                    _mostRecentChannel = await connection.CreateChannelAsync(cancellationToken: ct);
                }

                await callback(_mostRecentChannel!, cancellationToken);
            }
            catch (Exception e)
            {
                state.Exception = e;
                throw;
            }
        }, new ChannelState
        {
            Exception = null
        }, cancellationToken);
    }

    private sealed class ChannelState
    {
        public required Exception? Exception { get; set; }
    }
}