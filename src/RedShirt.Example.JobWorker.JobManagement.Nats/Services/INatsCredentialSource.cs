using RedShirt.Example.JobWorker.JobManagement.Nats.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

public interface INatsCredentialSource
{
    Task<NatsCredentialModel> GetCredentialsAsync(CancellationToken cancellationToken = default);
}