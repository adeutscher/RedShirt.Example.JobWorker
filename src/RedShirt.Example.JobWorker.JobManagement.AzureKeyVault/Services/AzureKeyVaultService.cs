using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Services;

public interface IAzureKeyVaultService
{
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}

internal class AzureKeyVaultService(IAzureKeyVaultClientSource clientSource) : IAzureKeyVaultService
{
    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        var source = clientSource.GetKeyVaultClient();
        return source.GetSecretAsync(secretName, cancellationToken);
    }
}