using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal interface INatsConsumerSource
{
    Task<INatsJSConsumer> GetConsumerAsync(bool forceNewConnection = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);

    void ResetConsumer();
}

internal class NatsConsumerSource(
    INatsConnectionCacheSource connectionCacheSource,
    IOptions<NatsStreamConfigurationModel> options,
    IOptions<NatsStreamTimeoutConfigurationModel> timeoutOptions) : INatsConsumerSource
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private INatsJSConsumer? _consumer;

    public async Task<INatsJSConsumer> GetConsumerAsync(bool forceNewConnection = false,
        bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!forceNewConnection && !forceNewSecretManagerPull && _consumer is not null)
            {
                return _consumer;
            }

            _consumer = null;

            var connectionResponse = await connectionCacheSource.GetConnectionAsync(forceNewConnection,
                forceNewSecretManagerPull, cancellationToken);
            _consumer = await connectionResponse.Client.Context.CreateOrUpdateConsumerAsync(options.Value.StreamName,
                new ConsumerConfig
                {
                    Name = options.Value.ConsumerName,
                    DurableName = options.Value.ConsumerName,
                    AckWait = TimeSpan.FromSeconds(timeoutOptions.Value.EffectiveVisibilityTimeoutSeconds)
                }, cancellationToken);
            return _consumer;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void ResetConsumer()
    {
        _consumer = null;
    }
}