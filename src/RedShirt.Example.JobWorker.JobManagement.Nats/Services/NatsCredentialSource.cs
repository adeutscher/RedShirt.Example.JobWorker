using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

public interface INatsCredentialSource
{
    Task<NatsCredentialModel> GetCredentialsAsync(CancellationToken cancellationToken = default);
}

internal class NatsCredentialSource(
    ISecretManagerCacheService secretManagerCacheService,
    IOptions<NatsCredentialSource.ConfigurationModel> options) : INatsCredentialSource
{
    public async Task<NatsCredentialModel> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var secrets = await secretManagerCacheService.GetSecretsAsync(
            [options.Value.UserPath, options.Value.PasswordPath],
            cancellationToken: cancellationToken);

        return new NatsCredentialModel
        {
            User = secrets[options.Value.UserPath],
            Password = secrets[options.Value.PasswordPath]
        };
    }

    public sealed class ConfigurationModel
    {
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}