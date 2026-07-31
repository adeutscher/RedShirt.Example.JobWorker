using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Factories;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetConnectionAsync_WrapsSecretManagerExceptionAsWorkerDistributedException(bool isTransient)
    {
        const string connectionStringPath = "redis/connection-string";
        var secretException = new WorkerSecretManagerException(
            "secret lookup failed",
            false,
            isTransient);

        var secrets = new Mock<ISecretManagerCacheService>(MockBehavior.Strict);
        secrets
            .Setup(s => s.GetSecretAsync(connectionStringPath, null, false, TestContext.Current.CancellationToken))
            .ThrowsAsync(secretException);

        var factory = new RedisConnectionFactory(
            secrets.Object,
            Options.Create(new RedisConnectionFactory.ConfigurationModel
            {
                ConnectionStringPath = connectionStringPath
            }));

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() =>
            factory.GetConnectionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(secretException.Message, thrown.Message);
        Assert.Same(secretException, thrown.InnerException);
        Assert.False(thrown.IsCritical);
        Assert.Equal(isTransient, thrown.IsTransient);
        secrets.Verify(s => s.GetSecretAsync(connectionStringPath, null, false, TestContext.Current.CancellationToken),
            Times.Once);
        secrets.VerifyNoOtherCalls();
    }
}