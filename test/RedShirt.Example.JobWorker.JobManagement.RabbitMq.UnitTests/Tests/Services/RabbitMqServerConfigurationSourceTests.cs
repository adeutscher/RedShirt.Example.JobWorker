using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqServerConfigurationSourceTests
{
    [Fact]
    public async Task GetConfigurationAsync_MapsHostSettingsAndResolvedSecrets()
    {
        var hostname = Guid.NewGuid().ToString("N");
        var vhost = Guid.NewGuid().ToString("N");
        var userPath = $"/rabbitmq/{Guid.NewGuid():N}/user";
        var passwordPath = $"/rabbitmq/{Guid.NewGuid():N}/password";
        var user = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(s => s.GetSecretsAsync(
                It.Is<List<string>>(keys =>
                    keys.Count == 2
                    && keys.Contains(userPath)
                    && keys.Contains(passwordPath)),
                null,
                false,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new SecretManagerCacheSecretsResponse
            {
                Values = new Dictionary<string, string>
                {
                    [userPath] = user,
                    [passwordPath] = password
                },
                QueriedSecretManager = true
            });

        var source = new RabbitMqServerConfigurationSource(
            secrets.Object,
            Options.Create(new RabbitMqServerConfigurationSource.ConfigurationModel
            {
                Hostname = hostname,
                VHost = vhost,
                UserPath = userPath,
                PasswordPath = passwordPath
            }));

        var configuration = await source.GetConfigurationAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(hostname, configuration.Hostname);
        Assert.Equal(vhost, configuration.VirtualHost);
        Assert.Equal(user, configuration.User);
        Assert.Equal(password, configuration.Password);
        secrets.Verify(s => s.GetSecretsAsync(
            It.IsAny<List<string>>(),
            null,
            false,
            TestContext.Current.CancellationToken), Times.Once);
        secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetConfigurationAsync_WhenForceNewSecretManagerPull_PassesForceTrue()
    {
        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(s => s.GetSecretsAsync(
                It.IsAny<List<string>>(),
                null,
                true,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new SecretManagerCacheSecretsResponse
            {
                Values = new Dictionary<string, string>
                {
                    ["/u"] = "user",
                    ["/p"] = "pass"
                },
                QueriedSecretManager = true
            });

        var source = new RabbitMqServerConfigurationSource(
            secrets.Object,
            Options.Create(new RabbitMqServerConfigurationSource.ConfigurationModel
            {
                Hostname = "h",
                VHost = "/",
                UserPath = "/u",
                PasswordPath = "/p"
            }));

        await source.GetConfigurationAsync(true, TestContext.Current.CancellationToken);

        secrets.Verify(s => s.GetSecretsAsync(
            It.IsAny<List<string>>(),
            null,
            true,
            TestContext.Current.CancellationToken), Times.Once);
    }
}