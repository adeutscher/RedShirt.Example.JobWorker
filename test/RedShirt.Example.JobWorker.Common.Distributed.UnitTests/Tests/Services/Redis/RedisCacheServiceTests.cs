using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis.Resilience;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisCacheServiceTests
{
    private static Mock<IDistributedRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string?>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string?>>, CancellationToken>((func, ct) => func(ct));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, ct) => func(ct));
        return retry;
    }

    private static (RedisCacheService Service, Mock<IDatabase> Database, Mock<IRedisConnectionCacheService> Connection,
        Mock<IDistributedRetryWrapperService> Retry) CreateService()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(database.Object);

        var retry = CreatePassthroughRetryWrapper();
        var service = new RedisCacheService(retry.Object, connection.Object);
        return (service, database, connection, retry);
    }

    [Fact]
    public async Task GetStringAsync_PassesCancellationTokenToRetryWrapper()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancellationToken? seenToken = null;

        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync("value");

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(cts.Token)).ReturnsAsync(database.Object);

        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string?>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string?>>, CancellationToken>((func, ct) =>
            {
                seenToken = ct;
                return func(ct);
            });

        var service = new RedisCacheService(retry.Object, connection.Object);

        await service.GetStringAsync("key", cts.Token);

        Assert.Equal(cts.Token, seenToken);
    }

    [Fact]
    public async Task GetStringAsync_ReturnsValueThroughRetryWrapper()
    {
        var (service, database, connection, retry) = CreateService();
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, result);
        connection.Verify(c => c.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        database.Verify(
            d => d.StringGetAsync(It.Is<RedisKey>(k => k == key), It.IsAny<CommandFlags>()), Times.Once);
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string?>>>(),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WhenDatabaseThrows_PropagatesThroughRetryWrapper()
    {
        var (service, database, _, _) = CreateService();
        var redisException = new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "offline", null, CommandStatus.Unknown);

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(redisException);

        var thrown = await Assert.ThrowsAsync<RedisConnectionException>(() =>
            service.GetStringAsync("key", TestContext.Current.CancellationToken));

        Assert.Same(redisException, thrown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task GetStringAsync_WhenMissingOrWhitespace_ReturnsNull(string? value)
    {
        var (service, database, _, _) = CreateService();
        var key = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(result);
        database.Verify(
            d => d.StringGetAsync(It.Is<RedisKey>(k => k == key), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_SetsValueWithExpiryThroughRetryWrapper()
    {
        var (service, database, connection, retry) = CreateService();
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromMinutes(4);

        database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await service.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken);

        connection.Verify(c => c.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        database.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == key),
            It.Is<RedisValue>(v => v == value),
            It.IsAny<Expiration>(),
            It.IsAny<ValueCondition>(),
            It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WhenDeleteFails_PropagatesThroughRetryWrapper()
    {
        var (service, database, _, _) = CreateService();
        var timeout = new TimeoutException("slow delete");

        database.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(timeout);

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() =>
            service.SetStringAsync("key", null, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(timeout, thrown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SetStringAsync_WhenNullOrEmpty_DeletesKey(string? value)
    {
        var (service, database, connection, retry) = CreateService();
        var key = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.EmptyString);

        await service.SetStringAsync(key, value, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        connection.Verify(c => c.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        database.Verify(
            d => d.StringGetDeleteAsync(It.Is<RedisKey>(k => k == key), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
            It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()), Times.Never);
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_WhenSetFails_PropagatesThroughRetryWrapper()
    {
        var (service, database, _, _) = CreateService();
        var redisException = new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "offline", null, CommandStatus.Unknown);

        database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(redisException);

        var thrown = await Assert.ThrowsAsync<RedisConnectionException>(() =>
            service.SetStringAsync("key", "value", TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(redisException, thrown);
    }
}