using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Docker.SecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Docker.SecretManager.Services;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Docker.SecretManager.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSecretManagerDocker_BindsConfigurationSection()
    {
        var directory = $"/tmp/{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ServiceCollectionExtensions.ConfigurationSectionName}:Directory"] = directory
            })
            .Build();

        var services = new ServiceCollection()
            .AddSecretManagerDocker(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DockerSecretManagerService.ConfigurationModel>>();

        Assert.Equal(directory, options.Value.Directory);
        Assert.Equal(directory, options.Value.EffectiveDirectory);
    }

    [Fact]
    public void AddSecretManagerDocker_RegistersExpectedSingleton()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection()
            .AddSecretManagerDocker(configuration);

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ISecretManagerService>();
        var second = provider.GetRequiredService<ISecretManagerService>();

        Assert.IsType<DockerSecretManagerService>(first);
        Assert.Same(first, second);
        Assert.Contains(services,
            d => d.ServiceType == typeof(ISecretManagerService)
                 && d.ImplementationType == typeof(DockerSecretManagerService)
                 && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddSecretManagerDocker_WhenDirectoryIsOmitted_UsesDefaultEffectiveDirectory()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection()
            .AddSecretManagerDocker(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DockerSecretManagerService.ConfigurationModel>>();

        Assert.Equal("/run/secrets", options.Value.EffectiveDirectory);
    }

    [Fact]
    public void ConfigurationSectionName_IsDockerSecretsSection()
    {
        Assert.Equal("Common:Secrets:Docker", ServiceCollectionExtensions.ConfigurationSectionName);
    }
}