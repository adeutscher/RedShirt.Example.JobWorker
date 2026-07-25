using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Services;

internal class AzureKeyVaultService(IAzureKeyVaultClientSource clientSource) : ISecretManagerService
{
    public Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var source = clientSource.GetKeyVaultClient();
        return source.GetSecretAsync(key, cancellationToken);
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys, CancellationToken cancellationToken = default)
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