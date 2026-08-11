using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal interface IActiveMqServerConfigurationSource
{
    Task<ActiveMqServerConfigurationModel> GetConfigurationAsync(CancellationToken cancellationToken = default);
}

internal class ActiveMqServerConfigurationSource(
    ISecretManagerCacheService secretManagerCacheService,
    IOptions<ActiveMqServerConfigurationSource.ConfigurationModel> options) : IActiveMqServerConfigurationSource
{
    public async Task<ActiveMqServerConfigurationModel> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var secrets = await secretManagerCacheService.GetSecretsAsync(
            [options.Value.UserPath, options.Value.PasswordPath],
            cancellationToken: cancellationToken);

        return new ActiveMqServerConfigurationModel
        {
            BrokerUri = options.Value.BrokerUri,
            User = secrets.Values[options.Value.UserPath],
            Password = secrets.Values[options.Value.PasswordPath]
        };
    }

    public sealed class ConfigurationModel
    {
        public required string BrokerUri { get; init; }
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}