using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class RedisShortTermIteratorStorageTests
{
    [Fact]
    public async Task Test_Get_WithNoResult()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var connectionSource = new Mock<IRedisConnectionSource>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabase())
            .Returns(database.Object);

        var key = Guid.NewGuid().ToString();
        string? value = null;

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        var storedValue = await storage.GetAsync(key);

        Assert.Equal(value, storedValue);

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
        var connectionSource = new Mock<IRedisConnectionSource>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabase())
            .Returns(database.Object);

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value);

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        var storedValue = await storage.GetAsync(key);

        Assert.Equal(value, storedValue);

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
        var connectionSource = new Mock<IRedisConnectionSource>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabase())
            .Returns(database.Object);

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        await storage.SetAsync(key, value);

        Assert.Single(database.Invocations);
        database.Verify(d =>
            d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d =>
            d.StringSetAsync(It.Is<RedisKey>(r => r.ToString().Contains(key)),
                It.Is<RedisValue>(r => r.ToString() == value), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(),
                It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Test_Set_Delete()
    {
        var database = new Mock<IDatabase>();
        var connectionSource = new Mock<IRedisConnectionSource>(MockBehavior.Strict);
        connectionSource.Setup(src => src.GetDatabase())
            .Returns(database.Object);

        var key = Guid.NewGuid().ToString();
        string? value = null;

        var storage = new RedisShortTermIteratorStorage(connectionSource.Object);
        await storage.SetAsync(key, value);

        Assert.Single(database.Invocations);
        database.Verify(d =>
            d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
        database.Verify(d =>
                d.StringGetDeleteAsync(It.Is<RedisKey>(r => r.ToString().Contains(key)), It.IsAny<CommandFlags>()),
            Times.Once);
    }
}