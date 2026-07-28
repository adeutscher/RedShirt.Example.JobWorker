using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class RedisShortTermIteratorStorageTests
{
    [Fact]
    public async Task Test_Get_WithNoResult()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var connectionSource = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(database.Object);

        var key = Guid.NewGuid().ToString();
        string? value = null;

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        var storedValue = await storage.GetAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, storedValue);

        Assert.Single(connectionSource.Invocations);
        connectionSource.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);

        Assert.Single(database.Invocations);
        database.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(
            d => d.StringGetAsync(It.Is<RedisKey>(r => r.ToString().Contains(key)), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task Test_Get_WithResult()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var connectionSource = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(database.Object);

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        var storedValue = await storage.GetAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, storedValue);

        Assert.Single(connectionSource.Invocations);
        connectionSource.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);

        Assert.Single(database.Invocations);
        database.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(
            d => d.StringGetAsync(It.Is<RedisKey>(r => r.ToString().Contains(key)), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task Test_Set()
    {
        var database = new Mock<IDatabase>();
        var connectionSource = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(database.Object);

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        await storage.SetAsync(key, value, TestContext.Current.CancellationToken);

        Assert.Single(database.Invocations);
        Assert.Single(connectionSource.Invocations);
        connectionSource.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        database.Verify(d =>
            d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d =>
            d.StringSetAsync(It.Is<RedisKey>(r => r.ToString().Contains(key)),
                It.Is<RedisValue>(r => r.ToString() == value), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(),
                It.IsAny<CommandFlags>()), Times.Once);
    }

    /// <summary>
    ///     Setting a null value to the key should be interpreted as a delete operation.
    /// </summary>
    [Fact]
    public async Task Test_Set_Delete()
    {
        var database = new Mock<IDatabase>();
        var connectionSource = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(database.Object);

        var key = Guid.NewGuid().ToString();
        string? value = null;

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        await storage.SetAsync(key, value, TestContext.Current.CancellationToken);

        Assert.Single(connectionSource.Invocations);
        connectionSource.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);

        Assert.Single(database.Invocations);
        database.Verify(d =>
            d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d =>
                d.StringGetDeleteAsync(It.Is<RedisKey>(r => r.ToString().Contains(key)), It.IsAny<CommandFlags>()),
            Times.Once);

        Assert.Single(connectionSource.Invocations);
        connectionSource.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}