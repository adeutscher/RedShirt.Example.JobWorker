using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services;

/// <summary>
///     Distributed a single shared RabbitMQ IChannel.
/// </summary>
internal interface IRabbitMqChannelCacheSource
{
    /// <param name="forceNewChannel">
    ///     When <c>true</c>, discard any cached channel and create a new one.
    /// </param>
    /// <param name="cancellationToken"></param>
    Task<IChannelWrapper> GetChannelAsync(Func<CancellationToken, Task> recoveryCallback, bool forceNewChannel = false,
        CancellationToken cancellationToken = default);
}

internal class RabbitMqChannelCacheSource(
    IRabbitMqConnectionFactory connectionFactory,
    IRabbitMqRetryWrapperService retryWrapperService) : IRabbitMqChannelCacheSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IChannelWrapper? _channelWrapper;
    private IConnection? _connection;

    private async Task OnRecoveryAsync(object obj, AsyncEventArgs args)
    {
        if (_channelWrapper is null)
        {
            return;
        }

        await _channelWrapper.OnRecoveryAsync(args.CancellationToken);
    }

    private async Task CleanUpOldChannelAsync(IChannel? oldChannel, CancellationToken cancellationToken)
    {
        if (oldChannel is null)
        {
            return;
        }

        try
        {
            await oldChannel.CloseAsync(cancellationToken);
        }
        catch
        {
            /*
             * Absorb and avoid any weird secondary crash
             * for closing a channel that we were disposing of anyway.
             */
        }

        await oldChannel.DisposeAsync();
        _channelWrapper = null;
    }

    public async Task<IChannelWrapper> GetChannelAsync(Func<CancellationToken, Task> recoveryCallback,
        bool forceNewChannel = false, CancellationToken cancellationToken = default)
    {
        // Deliberately not returning the cached wrapper outside the semaphore lock
        // Recovery callbacks are sensitive to making sure that the most recent callback is present.

        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewChannel && _channelWrapper is not null)
            {
                return _channelWrapper;
            }

            await CleanUpOldChannelAsync(_channelWrapper?.Channel, cancellationToken);

            if (_connection is null)
            {
                _connection = await connectionFactory.GetConnectionAsync(cancellationToken);
                /*
                 * The hardcoded AutomaticRecoveryEnabled property on the RabbitMQ connection parameters restores the connection
                 * and channel after a network failure. Topology recovery would also re-register consumers, so
                 * TopologyRecoveryEnabled is false: this worker does not declare topology (the queue is owned elsewhere),
                 * and StartConsumerAsync is the subscribe path (retry wrapper, logging, a new AsyncEventingBasicConsumer).
                 *
                 * If topology recovery had stayed on, then the client would restore the old consumer and this handler
                 * would BasicConsume again, leaving two competing consumers on the same queue.
                 * Re-subscribe here when the channel recovers.
                 */
                _connection.RecoverySucceededAsync += OnRecoveryAsync;
            }

            _channelWrapper = new ChannelWrapper
            {
                Channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken)
            };
            _channelWrapper.SetRecoveryCallback(recoveryCallback);
            return _channelWrapper;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}