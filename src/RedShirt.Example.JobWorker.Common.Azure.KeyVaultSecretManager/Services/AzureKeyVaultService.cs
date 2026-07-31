using Azure;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using System.Text.RegularExpressions;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Services;

internal partial class AzureKeyVaultService(IAzureKeyVaultClientSource clientSource) : ISecretManagerService
{
    private static bool IsValidKey(string key)
    {
        return ValidKeyRegex().IsMatch(key);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9-]{1,127}$")]
    private static partial Regex ValidKeyRegex();

    public Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(key))
        {
            throw new SecretManagerException($"Invalid secret key: {key}");
        }

        try
        {
            var client = clientSource.GetKeyVaultClient();
            return client.GetSecretAsync(key, cancellationToken);
        }
        catch (RequestFailedException e)
        {
            throw new SecretManagerException(e);
        }
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        CancellationToken cancellationToken = default)
    {
        var items = new Dictionary<string, string>();
        var source = clientSource.GetKeyVaultClient();

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var key in keys.Distinct())
        {
            items.Add(key, await source.GetSecretAsync(key, cancellationToken));
        }

        return items;
    }
}