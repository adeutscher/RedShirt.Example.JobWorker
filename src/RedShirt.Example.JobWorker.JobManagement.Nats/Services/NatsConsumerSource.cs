using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal interface INatsConsumerSource
{
    Task<INatsJSConsumer> GetConsumerAsync(CancellationToken cancellationToken = default);
}

internal class NatsConsumerSource(
    INatsJetStreamContextFactory contextFactory,
    IOptions<NatsStreamConfigurationModel> options) : INatsConsumerSource
{
    private readonly string _consumerName = Guid.NewGuid().ToString();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private INatsJSConsumer? _consumer;
    private INatsJSContext? _context;

    public async Task<INatsJSConsumer> GetConsumerAsync(CancellationToken cancellationToken = default)
    {
        if (_consumer is not null)
        {
            return _consumer;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _context ??= await contextFactory.CreateNatsJetStreamContextAsync(cancellationToken);
            _consumer ??= await _context.CreateOrUpdateConsumerAsync(options.Value.StreamName,
                new ConsumerConfig {Name = _consumerName}, cancellationToken);
            return _consumer;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}