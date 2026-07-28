using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Factories;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Factories;

public class RedisConnectionFactoryTests
{
    [Fact]
    public async Task GetConnectionAsync_ThrowsRedisConnectionException_WhenEndpointIsUnreachable()
    {
        const string connectionStringPath = "redis/connection-string";

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(s => s.GetSecretAsync(connectionStringPath, null, false, TestContext.Current.CancellationToken))
            .ReturnsAsync("localhost:1");

        var factory = new RedisConnectionFactory(
            secrets.Object,
            Options.Create(new RedisConnectionFactory.ConfigurationModel
            {
                ConnectionStringPath = connectionStringPath
            }));

        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            factory.GetConnectionAsync(TestContext.Current.CancellationToken));

        secrets.Verify(s => s.GetSecretAsync(connectionStringPath, null, false, TestContext.Current.CancellationToken),
            Times.Once);
        secrets.VerifyNoOtherCalls();
    }
}