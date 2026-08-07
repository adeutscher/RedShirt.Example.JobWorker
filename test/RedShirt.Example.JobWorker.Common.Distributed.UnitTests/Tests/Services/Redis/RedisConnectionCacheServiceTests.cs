using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Factories;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisConnectionCacheServiceTests
{
    private static Mock<IConnectionMultiplexer> CreateConnectedMultiplexer(Mock<IDatabase> database)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        multiplexer.SetupGet(m => m.IsConnected).Returns(true);
        multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        return multiplexer;
    }

    private static (Mock<IRedisConnectionFactory> Factory, Mock<IConnectionMultiplexer> Multiplexer, Mock<IDatabase>
        Database) CreateMocks()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var multiplexer = CreateConnectedMultiplexer(database);

        var factory = new Mock<IRedisConnectionFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(multiplexer.Object);

        return (factory, multiplexer, database);
    }

    [Fact]
    public async Task GetDatabaseAsync_ConcurrentCallers_CreateConnectionOnce()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var multiplexer = CreateConnectedMultiplexer(database);

        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<IConnectionMultiplexer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCallCount = 0;

        var factory = new Mock<IRedisConnectionFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                Interlocked.Increment(ref factoryCallCount);
                factoryEntered.TrySetResult();
                return await releaseFactory.Task.WaitAsync(cancellationToken);
            });

        var cache = new RedisConnectionCacheService(factory.Object);

        var first = cache.GetDatabaseAsync(TestContext.Current.CancellationToken);
        await factoryEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        var second = cache.GetDatabaseAsync(TestContext.Current.CancellationToken);
        // Allow the second caller to reach the lock while the first still holds it.
        await Task.Delay(50, TestContext.Current.CancellationToken);

        releaseFactory.SetResult(multiplexer.Object);

        var results = await Task.WhenAll(first, second);

        Assert.Same(database.Object, results[0]);
        Assert.Same(database.Object, results[1]);
        Assert.Equal(1, factoryCallCount);
        factory.Verify(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        multiplexer.Verify(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetDatabaseAsync_CreatesConnectionAndReturnsDatabase()
    {
        var (factory, multiplexer, database) = CreateMocks();
        var cache = new RedisConnectionCacheService(factory.Object);

        var result = await cache.GetDatabaseAsync(TestContext.Current.CancellationToken);

        Assert.Same(database.Object, result);
        factory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Once);
        multiplexer.Verify(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()), Times.Once);
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetDatabaseAsync_PropagatesFactoryException_AndAllowsRetry()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var multiplexer = CreateConnectedMultiplexer(database);

        var factory = new Mock<IRedisConnectionFactory>(MockBehavior.Strict);
        factory
            .SetupSequence(f => f.GetConnectionAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "boom", null, CommandStatus.Unknown))
            .ReturnsAsync(multiplexer.Object);

        var cache = new RedisConnectionCacheService(factory.Object);

        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            cache.GetDatabaseAsync(TestContext.Current.CancellationToken));

        var result = await cache.GetDatabaseAsync(TestContext.Current.CancellationToken);

        Assert.Same(database.Object, result);
        factory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Exactly(2));
    }

    [Fact]
    public async Task GetDatabaseAsync_ReusesCachedConnection_OnSubsequentCalls()
    {
        var (factory, multiplexer, database) = CreateMocks();
        var cache = new RedisConnectionCacheService(factory.Object);

        var first = await cache.GetDatabaseAsync(TestContext.Current.CancellationToken);
        var second = await cache.GetDatabaseAsync(TestContext.Current.CancellationToken);

        Assert.Same(database.Object, first);
        Assert.Same(database.Object, second);
        factory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Once);
        multiplexer.Verify(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()), Times.Exactly(2));
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetDatabaseAsync_ThrowsWhenAlreadyCancelled()
    {
        var factory = new Mock<IRedisConnectionFactory>(MockBehavior.Strict);
        var cache = new RedisConnectionCacheService(factory.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.GetDatabaseAsync(cts.Token));

        factory.VerifyNoOtherCalls();
    }
}