using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqServerConfigurationSourceTests
{
    [Fact]
    public async Task GetConfigurationAsync_MapsBrokerUriAndResolvedSecrets()
    {
        var brokerUri = $"tcp://{Guid.NewGuid():N}:61616";
        var userPath = $"/activemq/{Guid.NewGuid():N}/user";
        var passwordPath = $"/activemq/{Guid.NewGuid():N}/password";
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

        var source = new ActiveMqServerConfigurationSource(
            secrets.Object,
            Options.Create(new ActiveMqServerConfigurationSource.ConfigurationModel
            {
                BrokerUri = brokerUri,
                UserPath = userPath,
                PasswordPath = passwordPath
            }));

        var configuration = await source.GetConfigurationAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(brokerUri, configuration.BrokerUri);
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
    public async Task GetConfigurationAsync_WhenForceNewSecretManagerPull_PassesForceToCache()
    {
        var brokerUri = $"tcp://{Guid.NewGuid():N}:61616";
        var userPath = $"/activemq/{Guid.NewGuid():N}/user";
        var passwordPath = $"/activemq/{Guid.NewGuid():N}/password";

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
                    [userPath] = "u",
                    [passwordPath] = "p"
                },
                QueriedSecretManager = true
            });

        var source = new ActiveMqServerConfigurationSource(
            secrets.Object,
            Options.Create(new ActiveMqServerConfigurationSource.ConfigurationModel
            {
                BrokerUri = brokerUri,
                UserPath = userPath,
                PasswordPath = passwordPath
            }));

        await source.GetConfigurationAsync(true, TestContext.Current.CancellationToken);

        secrets.Verify(s => s.GetSecretsAsync(
            It.IsAny<List<string>>(),
            null,
            true,
            TestContext.Current.CancellationToken), Times.Once);
    }
}