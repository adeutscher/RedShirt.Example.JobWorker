using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

public interface IRabbitMqServerConfigurationSource
{
    Task<RabbitMqServerConfigurationModel> GetConfigurationAsync(CancellationToken cancellationToken = default);
}