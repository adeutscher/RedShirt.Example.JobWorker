using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.Azure.Services;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using System.Text.RegularExpressions;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Services;

internal partial class AzureKeyVaultService(
    IAzureRetryWrapperService retryWrapperService,
    IAzureKeyVaultClientSource clientSource) : ISecretManagerService
{
    private static bool IsValidKey(string key)
    {
        return ValidKeyRegex().IsMatch(key);
    }

    /// <summary>
    ///     Regular expression for Azure Key Vault resources.
    ///     An Azure Key Vault key name must be a 1 to 127 character string containing only alphanumeric characters (0-9, a-z,
    ///     A-Z) and hyphens (-)
    ///     Source: https://learn.microsoft.com/en-us/azure/key-vault/general/about-keys-secrets-certificates
    /// </summary>
    /// <returns></returns>
    [GeneratedRegex(@"^[a-zA-Z0-9-]{1,127}$")]
    private static partial Regex ValidKeyRegex();

    public async Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(key))
        {
            // ReSharper disable once RedundantArgumentDefaultValue
            throw new WorkerSecretManagerException($"Invalid secret path: {key}", true);
        }

        try
        {
            return await retryWrapperService.RunAsync(ct =>
            {
                var client = clientSource.GetKeyVaultClient();
                return client.GetSecretAsync(key, ct);
            }, cancellationToken);
        }
        catch (WorkerAzureException e)
        {
            // Translate
            throw new WorkerSecretManagerException(e, e.IsCritical, e.IsTransient);
        }
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        CancellationToken cancellationToken = default)
    {
        if (keys.FirstOrDefault(key => !IsValidKey(key)) is { } badKey)
        {
            // ReSharper disable once RedundantArgumentDefaultValue
            throw new WorkerSecretManagerException($"Invalid secret path: {badKey}", true);
        }

        var items = new Dictionary<string, string>();

        try
        {
            var source = await retryWrapperService.RunAsync(_ => Task.FromResult(clientSource.GetKeyVaultClient()),
                cancellationToken);
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var key in keys.Distinct())
            {
                items.Add(key,
                    await retryWrapperService.RunAsync(ct => source.GetSecretAsync(key, ct), cancellationToken));
            }
        }
        catch (WorkerAzureException e)
        {
            // Translate
            throw new WorkerSecretManagerException(e, e.IsCritical, e.IsTransient);
        }

        return items;
    }
}