using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

public interface IRabbitMqServerConfigurationSource
{
    Task<RabbitMqServerConfigurationModel> GetConfigurationAsync(CancellationToken cancellationToken = default);
}

internal sealed class RabbitMqServerConfigurationSource(
    ISecretManagerCacheService secretManagerCacheService,
    IOptions<RabbitMqServerConfigurationSource.ConfigurationModel> options) : IRabbitMqServerConfigurationSource
{
    public async Task<RabbitMqServerConfigurationModel> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var secrets = await secretManagerCacheService.GetSecretsAsync(
            [options.Value.UserPath, options.Value.PasswordPath],
            cancellationToken: cancellationToken);

        return new RabbitMqServerConfigurationModel
        {
            Hostname = options.Value.Hostname,
            VirtualHost = options.Value.VHost,
            User = secrets[options.Value.UserPath],
            Password = secrets[options.Value.PasswordPath]
        };
    }

    public sealed class ConfigurationModel
    {
        public required string Hostname { get; init; }
        public required string VHost { get; init; }
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}