using Azure.Security.KeyVault.Secrets;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;

internal interface IAzureKeyVaultClientWrapper
{
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}

internal sealed class AzureKeyVaultClientWrapper(SecretClient secretClient) : IAzureKeyVaultClientWrapper
{
    internal SecretClient Client => secretClient;

    public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        var secret = await Client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return secret.Value.Value;
    }
}