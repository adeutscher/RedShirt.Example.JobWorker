using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.Extensions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddCommon()
            .AddSecretManagerAzureKeyVault(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddSecretManagerAzureKeyVault_LiveMode_DoesNotNeedCaCertificatePath()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Common:Secrets:AzureKeyVault:KeyVaultUrl"] = "https://test.vault.azure.net/",
            ["Common:Secrets:AzureKeyVault:GenerateLocalTestingToken"] = "false"
        });

        var options = provider.GetRequiredService<IOptions<AzureKeyVaultClientFactory.ConfigurationModel>>().Value;
        Assert.Null(options.LocalTestingCaCertificatePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/nonexistent/azure-key-vault-ca/emulator.local.crt")]
    public void AddSecretManagerAzureKeyVault_LocalTestingWithoutCaFile_FailsValidation(string? caPath)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Common:Secrets:AzureKeyVault:KeyVaultUrl"] = "https://localhost:4997/",
            ["Common:Secrets:AzureKeyVault:GenerateLocalTestingToken"] = "true",
            ["Common:Secrets:AzureKeyVault:LocalTestingCaCertificatePath"] = caPath
        });

        var thrown = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AzureKeyVaultClientFactory.ConfigurationModel>>().Value);
        Assert.Contains("LocalTestingCaCertificatePath", thrown.Message);
    }

    [Fact]
    public void AddSecretManagerAzureKeyVault_RegistersExpectedSingletons()
    {
        const string vaultUrl = "https://test.vault.azure.net/";
        var caPath = Path.GetTempFileName();

        try
        {
            using var provider = BuildProvider(new Dictionary<string, string?>
            {
                ["Common:Secrets:AzureKeyVault:KeyVaultUrl"] = vaultUrl,
                ["Common:Secrets:AzureKeyVault:GenerateLocalTestingToken"] = "true",
                ["Common:Secrets:AzureKeyVault:LocalTestingCaCertificatePath"] = caPath
            });

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
            Assert.Equal(caPath, retrievedConfiguration.Value.LocalTestingCaCertificatePath);
        }
        finally
        {
            File.Delete(caPath);
        }
    }

    [Fact]
    public void AddSecretManagerAzureKeyVault_RegistersStartupValidation()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection()
            .AddSecretManagerAzureKeyVault(configuration);

        // ValidateOnStart registers its check through the hosting startup validator, so the host fails fast.
        Assert.Contains(services, d => d.ServiceType.Name == "IStartupValidator");
    }
}