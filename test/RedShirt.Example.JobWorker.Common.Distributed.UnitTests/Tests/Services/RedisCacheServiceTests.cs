using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class RedisCacheServiceTests
{
    private static (RedisCacheService Service, Mock<IDatabase> Database, Mock<IRedisConnectionCacheService> Connection)
        CreateService()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(database.Object);

        return (new RedisCacheService(connection.Object), database, connection);
    }

    [Fact]
    public async Task GetStringAsync_ReturnsValue()
    {
        var (service, database, connection) = CreateService();
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, result);
        connection.Verify(c => c.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        database.Verify(
            d => d.StringGetAsync(It.Is<RedisKey>(k => k == key), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WhenConnectionSourceFails_ThrowsCacheConnectionException()
    {
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var redisException = new RedisConnectionException(ConnectionFailureType.SocketFailure, "socket");
        connection.Setup(c => c.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(redisException);

        var service = new RedisCacheService(connection.Object);

        var exception = await Assert.ThrowsAsync<CacheConnectionException>(() =>
            service.GetStringAsync("key", TestContext.Current.CancellationToken));

        Assert.Same(redisException, exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task GetStringAsync_WhenMissingOrWhitespace_ReturnsNull(string? value)
    {
        var (service, database, _) = CreateService();
        var key = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(result);
        database.Verify(
            d => d.StringGetAsync(It.Is<RedisKey>(k => k == key), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetStringAsync_WhenRedisConnectionFails_ThrowsCacheConnectionException()
    {
        var (service, database, _) = CreateService();
        var key = Guid.NewGuid().ToString();
        var redisException = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "offline");

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(redisException);

        var exception = await Assert.ThrowsAsync<CacheConnectionException>(() =>
            service.GetStringAsync(key, TestContext.Current.CancellationToken));

        Assert.Same(redisException, exception.InnerException);
    }

    [Fact]
    public async Task GetStringAsync_WhenTimedOut_ThrowsCacheTimeoutException()
    {
        var (service, database, _) = CreateService();
        var key = Guid.NewGuid().ToString();
        var timeout = new TimeoutException("slow redis");

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(timeout);

        var exception = await Assert.ThrowsAsync<CacheTimeoutException>(() =>
            service.GetStringAsync(key, TestContext.Current.CancellationToken));

        Assert.Same(timeout, exception.InnerException);
    }

    [Fact]
    public async Task SetStringAsync_SetsValueWithExpiry()
    {
        var (service, database, connection) = CreateService();
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
    }

    [Fact]
    public async Task SetStringAsync_WhenDeleteTimedOut_ThrowsCacheTimeoutException()
    {
        var (service, database, _) = CreateService();
        var timeout = new TimeoutException("slow delete");

        database.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(timeout);

        var exception = await Assert.ThrowsAsync<CacheTimeoutException>(() =>
            service.SetStringAsync("key", null, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(timeout, exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SetStringAsync_WhenNullOrEmpty_DeletesKey(string? value)
    {
        var (service, database, connection) = CreateService();
        var key = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.EmptyString);

        await service.SetStringAsync(key, value, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        connection.Verify(c => c.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        database.Verify(
            d => d.StringGetDeleteAsync(It.Is<RedisKey>(k => k == key), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
            It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task SetStringAsync_WhenRedisConnectionFails_ThrowsCacheConnectionException()
    {
        var (service, database, _) = CreateService();
        var redisException = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "offline");

        database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(redisException);

        var exception = await Assert.ThrowsAsync<CacheConnectionException>(() =>
            service.SetStringAsync("key", "value", TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(redisException, exception.InnerException);
    }

    [Fact]
    public async Task SetStringAsync_WhenTimedOut_ThrowsCacheTimeoutException()
    {
        var (service, database, _) = CreateService();
        var timeout = new TimeoutException("slow redis");

        database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(timeout);

        var exception = await Assert.ThrowsAsync<CacheTimeoutException>(() =>
            service.SetStringAsync("key", "value", TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(timeout, exception.InnerException);
    }
}