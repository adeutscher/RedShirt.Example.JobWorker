namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;

/// <summary>
/// Resolved configuration as distributed by the configuration source service.
/// </summary>
public sealed class RabbitMqServerConfigurationModel
{
    public required string Hostname { get; init; }
    public required string VirtualHost { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
}