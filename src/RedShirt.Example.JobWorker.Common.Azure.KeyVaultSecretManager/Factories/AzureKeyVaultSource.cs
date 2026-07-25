using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;

internal interface IAzureKeyVaultClientSource
{
    IAzureKeyVaultClientWrapper GetKeyVaultClient();
}

internal class AzureKeyVaultClientSource(IAzureKeyVaultClientFactory factory) : IAzureKeyVaultClientSource
{
    private readonly Lazy<IAzureKeyVaultClientWrapper> _queueClient = new(factory.GetClient);

    public IAzureKeyVaultClientWrapper GetKeyVaultClient()
    {
        return _queueClient.Value;
    }
}