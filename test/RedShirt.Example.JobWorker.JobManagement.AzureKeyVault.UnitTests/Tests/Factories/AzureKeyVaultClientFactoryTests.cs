using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.UnitTests.Tests.Factories;

public class AzureKeyVaultClientFactoryTests
{
    [Fact]
    public void GetClient()
    {
        var config = new AzureKeyVaultClientFactory.ConfigurationModel
        {
            KeyVaultUrl = "https://foo",
            GenerateLocalTestingToken = false
        };

        var factory = new AzureKeyVaultClientFactory(Options.Create(config));

        var client = factory.GetClient();
        Assert.NotNull(client);

        Assert.True(client is AzureKeyVaultClientWrapper);
        var clientWrapperImplementation = client as AzureKeyVaultClientWrapper;
        Assert.NotNull(clientWrapperImplementation);
        Assert.NotNull(clientWrapperImplementation.Client);
    }
}