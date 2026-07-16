using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;

public interface IAzureKeyVaultClientSource
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