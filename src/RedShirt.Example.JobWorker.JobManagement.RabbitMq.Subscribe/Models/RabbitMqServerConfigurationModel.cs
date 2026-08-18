namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;

public sealed class RabbitMqServerConfigurationModel
{
    public required string Hostname { get; init; }
    public required string VirtualHost { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
}