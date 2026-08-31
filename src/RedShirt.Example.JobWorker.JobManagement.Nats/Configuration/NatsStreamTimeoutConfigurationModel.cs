namespace RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;

public class NatsStreamTimeoutConfigurationModel
{
    public required int VisibilityTimeoutSeconds { get; init; }

    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);
}