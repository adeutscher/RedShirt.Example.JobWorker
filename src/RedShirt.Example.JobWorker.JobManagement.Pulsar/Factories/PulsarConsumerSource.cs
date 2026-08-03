using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;

internal interface IPulsarConsumerSource
{
    Task<IPulsarConsumerWrapper> GetConsumerAsync(CancellationToken cancellationToken = default);
}

internal class PulsarConsumerSource(IPulsarConsumerFactory factory) : IPulsarConsumerSource
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPulsarConsumerWrapper? _consumer;

    public async Task<IPulsarConsumerWrapper> GetConsumerAsync(CancellationToken cancellationToken = default)
    {
        if (_consumer is not null)
        {
            return _consumer;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_consumer is not null)
            {
                return _consumer;
            }

            _consumer = await factory.CreateConsumerAsync(cancellationToken);
            return _consumer;
        }
        finally
        {
            _lock.Release();
        }
    }
}