using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqChannelCacheSourceTests
{
    private static (RabbitMqChannelCacheSource Source, Mock<IRabbitMqConnectionFactory> ConnectionFactory,
        Mock<IConnection> Connection, Mock<IChannel> Channel)
        CreateSut()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var connection = new Mock<IConnection>(MockBehavior.Strict);
        connection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel.Object);

        var connectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        connectionFactory
            .Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var source = new RabbitMqChannelCacheSource(connectionFactory.Object,
            RabbitMqRetryTestHelpers.CreatePassthroughRetryWrapper().Object);

        return (source, connectionFactory, connection, channel);
    }

    [Fact]
    public async Task GetChannelAsync_ConcurrentCallers_ShareSingleChannel()
    {
        var (source, connectionFactory, connection, channel) = CreateSut();

        var results = await Task.WhenAll(
            source.GetChannelAsync(TestContext.Current.CancellationToken),
            source.GetChannelAsync(TestContext.Current.CancellationToken),
            source.GetChannelAsync(TestContext.Current.CancellationToken));

        Assert.All(results, result => Assert.Same(channel.Object, result));
        connectionFactory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Once);
        connection.Verify(
            c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetChannelAsync_CreatesChannelOnceAndReusesIt()
    {
        var (source, connectionFactory, connection, channel) = CreateSut();

        connectionFactory.Verify(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
        connection.Verify(
            c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var first = await source.GetChannelAsync(TestContext.Current.CancellationToken);
        Assert.Same(channel.Object, first);

        connectionFactory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Once);
        connection.Verify(
            c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), TestContext.Current.CancellationToken),
            Times.Once);

        var second = await source.GetChannelAsync(TestContext.Current.CancellationToken);
        Assert.Same(first, second);

        connectionFactory.Verify(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(
            c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetChannelAsync_PassesCancellationTokenToConnectionAndRetry()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var capturedTokens = new List<CancellationToken>();

        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var connection = new Mock<IConnection>(MockBehavior.Strict);
        connection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .Returns<CreateChannelOptions?, CancellationToken>((_, token) =>
            {
                capturedTokens.Add(token);
                return Task.FromResult(channel.Object);
            });

        var connectionFactory = new Mock<IRabbitMqConnectionFactory>(MockBehavior.Strict);
        connectionFactory
            .Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(token =>
            {
                capturedTokens.Add(token);
                return Task.FromResult(connection.Object);
            });

        var retry = RabbitMqRetryTestHelpers.CreatePassthroughRetryWrapper();
        var source = new RabbitMqChannelCacheSource(connectionFactory.Object, retry.Object);

        var result = await source.GetChannelAsync(cts.Token);

        Assert.Same(channel.Object, result);
        Assert.Equal(2, capturedTokens.Count);
        Assert.All(capturedTokens, token => Assert.Equal(cts.Token, token));
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IChannel>>>(), cts.Token),
            Times.Once);
    }
}