using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.Core.Extensions;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSecretManagerAzureKeyVault_RegistersExpectedSingletons()
    {
        const string vaultUrl = "https://test.vault.azure.net/";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Common:Secrets:AzureKeyVault:KeyVaultUrl"] = vaultUrl,
                ["Common:Secrets:AzureKeyVault:GenerateLocalTestingToken"] = "true"
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration)
            .AddSecretManagerAzureKeyVault(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IAzureKeyVaultClientFactory>());
        Assert.NotNull(provider.GetService<IAzureKeyVaultClientSource>());
        Assert.NotNull(provider.GetService<ISecretManagerService>());
        Assert.Same(
            provider.GetRequiredService<ISecretManagerService>(),
            provider.GetRequiredService<ISecretManagerService>());

        var retrievedConfiguration =
            provider.GetRequiredService<IOptions<AzureKeyVaultClientFactory.ConfigurationModel>>();
        Assert.NotNull(retrievedConfiguration);
        Assert.Equal(vaultUrl, retrievedConfiguration.Value.KeyVaultUrl);
    }
}