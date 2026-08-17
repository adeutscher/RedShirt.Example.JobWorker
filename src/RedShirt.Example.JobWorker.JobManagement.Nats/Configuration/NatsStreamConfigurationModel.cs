namespace RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;

internal class NatsStreamConfigurationModel
{
    public required string StreamName { get; init; }

    /// <summary>
    ///     Sets unique name of the consumer.
    ///     The same consumer name value should be shared by all instances of this JobWorker implementation.
    ///     If the consumer name were unique per-instance, then each instance would lose track of their place in the stream.
    /// </summary>
    public required string ConsumerName { get; init; }
}