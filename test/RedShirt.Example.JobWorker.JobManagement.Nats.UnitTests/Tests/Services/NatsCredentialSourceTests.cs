using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsCredentialSourceTests
{
    [Fact]
    public async Task GetCredentialsAsync_ResolvesUserAndPasswordFromSecretCache()
    {
        var userPath = $"/nats/{Guid.NewGuid():N}/user";
        var passwordPath = $"/nats/{Guid.NewGuid():N}/password";
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

        var source = new NatsCredentialSource(
            secrets.Object,
            Options.Create(new NatsCredentialSource.ConfigurationModel
            {
                UserPath = userPath,
                PasswordPath = passwordPath
            }));

        var credentials = await source.GetCredentialsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(user, credentials.User);
        Assert.Equal(password, credentials.Password);
        secrets.Verify(s => s.GetSecretsAsync(
            It.IsAny<List<string>>(),
            null,
            false,
            TestContext.Current.CancellationToken), Times.Once);
        secrets.VerifyNoOtherCalls();
    }
}