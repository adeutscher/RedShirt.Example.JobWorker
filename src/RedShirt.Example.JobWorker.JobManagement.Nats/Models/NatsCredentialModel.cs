namespace RedShirt.Example.JobWorker.JobManagement.Nats.Models;

public sealed class NatsCredentialModel
{
    public required string User { get; init; }
    public required string Password { get; init; }
}