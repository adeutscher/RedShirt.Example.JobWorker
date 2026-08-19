using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Factories;
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
    Task<IChannel> GetChannelAsync(bool forceNewChannel = false, CancellationToken cancellationToken = default);
}

internal class RabbitMqChannelCacheSource(
    IRabbitMqConnectionFactory connectionFactory,
    IRabbitMqRetryWrapperService retryWrapperService) : IRabbitMqChannelCacheSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task<IChannel> GetChannelAsync(bool forceNewChannel = false, CancellationToken cancellationToken = default)
    {
        if (!forceNewChannel && _channel is not null)
        {
            return _channel;
        }

        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewChannel && _channel is not null)
            {
                return _channel;
            }

            await CleanUpChannelAsync(cancellationToken);

            return await retryWrapperService.RunAsync(async ct =>
            {
                _connection ??= await connectionFactory.GetConnectionAsync(ct);
                _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
                return _channel;
            }, cancellationToken);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
    
    private async Task CleanUpChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }
        
        try
        {
            await _channel.CloseAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            /*
             * Swallow any weird avoid secondary crash
             * for closing a channel that we were disposing of anyway.
            */
        }
        await _channel.DisposeAsync();
        _channel = null;
    }
}