using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqConnectionCacheSourceTests
{
    [Fact]
    public async Task GetConnectionAsync_ConcurrentCallers_ShareSingleCreatedConnection()
    {
        var connection = new Mock<IConnection>(MockBehavior.Strict);
        var factory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken))
            .Returns(async (bool _, CancellationToken ct) =>
            {
                await Task.Delay(25, ct);
                return connection.Object;
            });

        var source = new RabbitMqConnectionCacheSource(factory.Object);

        var results = await Task.WhenAll(
            source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken),
            source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken),
            source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken));

        Assert.Single(results, r => !r.CachedConnection);
        Assert.Equal(2, results.Count(r => r.CachedConnection));
        Assert.All(results, r => Assert.Same(connection.Object, r.Connection));
        factory.Verify(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetConnectionAsync_FirstCall_CreatesUncachedConnection()
    {
        var connection = new Mock<IConnection>(MockBehavior.Strict);
        var factory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken))
            .ReturnsAsync(connection.Object);

        var source = new RabbitMqConnectionCacheSource(factory.Object);

        var result = await source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken);

        Assert.False(result.CachedConnection);
        Assert.Same(connection.Object, result.Connection);
        factory.Verify(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetConnectionAsync_SecondCallWithoutForce_ReturnsCachedConnection()
    {
        var connection = new Mock<IConnection>(MockBehavior.Strict);
        var factory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken))
            .ReturnsAsync(connection.Object);

        var source = new RabbitMqConnectionCacheSource(factory.Object);

        var first = await source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken);
        var second = await source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken);

        Assert.False(first.CachedConnection);
        Assert.True(second.CachedConnection);
        Assert.Same(first.Connection, second.Connection);
        factory.Verify(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetConnectionAsync_WhenForceNewConnection_CreatesReplacement()
    {
        var firstConnection = new Mock<IConnection>(MockBehavior.Strict);
        var secondConnection = new Mock<IConnection>(MockBehavior.Strict);
        var factory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        factory
            .SetupSequence(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken))
            .ReturnsAsync(firstConnection.Object)
            .ReturnsAsync(secondConnection.Object);

        var source = new RabbitMqConnectionCacheSource(factory.Object);

        var first = await source.GetConnectionAsync(false, false, TestContext.Current.CancellationToken);
        var forced = await source.GetConnectionAsync(true, false, TestContext.Current.CancellationToken);

        Assert.Same(firstConnection.Object, first.Connection);
        Assert.False(forced.CachedConnection);
        Assert.Same(secondConnection.Object, forced.Connection);
        factory.Verify(f => f.GetConnectionAsync(false, TestContext.Current.CancellationToken), Times.Exactly(2));
    }
}