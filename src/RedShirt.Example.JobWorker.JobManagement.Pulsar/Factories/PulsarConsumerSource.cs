using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Factories;

internal interface IPulsarConsumerSource
{
    IPulsarConsumerWrapper GetConsumer();
}

internal class PulsarConsumerSource(IPulsarConsumerFactory factory) : IPulsarConsumerSource
{
    private readonly Lazy<IPulsarConsumerWrapper> _consumer = new(factory.CreateConsumer);

    public IPulsarConsumerWrapper GetConsumer()
    {
        return _consumer.Value;
    }
}
