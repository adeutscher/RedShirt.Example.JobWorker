namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

public sealed class ActiveMqServerConfigurationModel
{
    public required string BrokerUri { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
}