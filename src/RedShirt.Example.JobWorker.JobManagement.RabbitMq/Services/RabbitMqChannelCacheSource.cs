using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Distributed a single shared RabbitMQ IChannel.
/// </summary>
internal interface IRabbitMqChannelCacheSource
{
    Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default);
}

internal class RabbitMqChannelCacheSource(
    IRabbitMqConnectionFactory connectionFactory,
    IRabbitMqRetryWrapperService retryWrapperService) : IRabbitMqChannelCacheSource
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private IChannel? _channel;

    public async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_channel is not null)
        {
            return _channel;
        }

        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null)
            {
                return _channel;
            }

            return await retryWrapperService.RunAsync(async ct =>
            {
                var connection = await connectionFactory.GetConnectionAsync(ct);
                _channel = await connection.CreateChannelAsync(cancellationToken: ct);
                return _channel;
            }, cancellationToken);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}