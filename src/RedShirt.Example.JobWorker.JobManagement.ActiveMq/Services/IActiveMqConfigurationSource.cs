using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

public interface IActiveMqServerConfigurationSource
{
    Task<ActiveMqServerConfigurationModel> GetConfigurationAsync(CancellationToken cancellationToken = default);
}