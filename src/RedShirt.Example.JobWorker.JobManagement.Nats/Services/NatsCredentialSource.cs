using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services;

internal interface INatsCredentialSource
{
    Task<NatsCredentialModel> GetCredentialsAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class NatsCredentialSource(
    ISecretManagerCacheService secretManagerCacheService,
    IOptions<NatsCredentialSource.ConfigurationModel> options) : INatsCredentialSource
{
    public async Task<NatsCredentialModel> GetCredentialsAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var secrets = await secretManagerCacheService.GetSecretsAsync(
            [options.Value.UserPath, options.Value.PasswordPath],
            force: forceNewSecretManagerPull,
            cancellationToken: cancellationToken);

        return new NatsCredentialModel
        {
            User = secrets.Values[options.Value.UserPath],
            Password = secrets.Values[options.Value.PasswordPath]
        };
    }

    public sealed class ConfigurationModel
    {
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}