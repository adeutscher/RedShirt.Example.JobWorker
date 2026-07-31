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

    public Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(key))
        {
            throw new SecretManagerException($"Invalid secret path: {key}");
        }

        return retryWrapperService.RunAsync(ct =>
        {
            var client = clientSource.GetKeyVaultClient();
            return client.GetSecretAsync(key, ct);
        }, cancellationToken);
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        CancellationToken cancellationToken = default)
    {
        if (keys.FirstOrDefault(key => !IsValidKey(key)) is { } badKey)
        {
            throw new SecretManagerException($"Invalid secret path: {badKey}");
        }

        var items = new Dictionary<string, string>();

        var source = await retryWrapperService.RunAsync(_ => Task.FromResult(clientSource.GetKeyVaultClient()),
            cancellationToken);
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var key in keys.Distinct())
        {
            items.Add(key, await retryWrapperService.RunAsync(ct => source.GetSecretAsync(key, ct), cancellationToken));
        }

        return items;
    }
}