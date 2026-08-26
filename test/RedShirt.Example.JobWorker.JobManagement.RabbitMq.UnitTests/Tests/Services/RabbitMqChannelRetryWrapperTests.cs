using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;
using System.Runtime.ExceptionServices;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqChannelRetryWrapperTests
{
    private static OperationInterruptedException Interrupted(ushort replyCode)
    {
        return new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, replyCode, "test"));
    }

    private static (RabbitMqChannelRetryWrapper Wrapper, Mock<IRabbitMqConnectionCacheSource> Cache,
        Mock<IConnection> Connection, Mock<IRabbitMqExceptionArbiterService> Arbiter)
        CreateWrapper(IRabbitMqRetryWrapperService retry, IChannel? channel = null)
    {
        var connection = new Mock<IConnection>(MockBehavior.Strict);
        if (channel is not null)
        {
            connection
                .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(channel);
        }

        var cache = new Mock<IRabbitMqConnectionCacheSource>(MockBehavior.Strict);
        var arbiter = new Mock<IRabbitMqExceptionArbiterService>(MockBehavior.Strict);
        var wrapper = new RabbitMqChannelRetryWrapper(retry, cache.Object, arbiter.Object);
        return (wrapper, cache, connection, arbiter);
    }

    [Fact]
    public async Task
        GetChannelAndDoActionWithRetryAsync_WhenCachedConnection_ReusesChannelAndSkipsNewConnectionCallback()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper();
        var (wrapper, cache, connection, _) = CreateWrapper(retry, channel.Object);

        cache
            .SetupSequence(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            })
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = true,
                Connection = connection.Object
            });

        var newConnectionCalls = 0;
        Action<IConnection> onNewConnection = _ => newConnectionCalls++;

        await wrapper.GetChannelAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            onNewConnectionCallback: onNewConnection,
            cancellationToken: TestContext.Current.CancellationToken);
        await wrapper.GetChannelAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
            onNewConnectionCallback: onNewConnection,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, newConnectionCalls);
        connection.Verify(
            c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task
        GetChannelAndDoActionWithRetryAsync_WhenChannelInterrupted_RecreatesChannelWithoutForcingConnection()
    {
        var firstChannel = new Mock<IChannel>(MockBehavior.Strict);
        var secondChannel = new Mock<IChannel>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper(2);
        var (wrapper, cache, connection, _) = CreateWrapper(retry);

        connection
            .SetupSequence(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstChannel.Object)
            .ReturnsAsync(secondChannel.Object);

        cache
            .SetupSequence(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            })
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = true,
                Connection = connection.Object
            });

        var attempts = 0;
        var seenChannels = new List<IChannel>();
        var newConnectionCalls = 0;

        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (ch, _) =>
            {
                attempts++;
                seenChannels.Add(ch);
                if (attempts == 1)
                {
                    throw Interrupted(404);
                }

                return Task.CompletedTask;
            },
            onNewConnectionCallback: _ => newConnectionCalls++,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([firstChannel.Object, secondChannel.Object], seenChannels);
        Assert.Equal(1, newConnectionCalls);
        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Exactly(2));
        cache.Verify(c => c.GetConnectionAsync(true, false, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData((ushort) 320)]
    [InlineData((ushort) 541)]
    public async Task GetChannelAndDoActionWithRetryAsync_WhenConnectionInterrupted_ForcesNewConnectionAndChannel(
        ushort replyCode)
    {
        var firstChannel = new Mock<IChannel>(MockBehavior.Strict);
        var secondChannel = new Mock<IChannel>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper(2);
        var (wrapper, cache, connection, _) = CreateWrapper(retry);

        connection
            .SetupSequence(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstChannel.Object)
            .ReturnsAsync(secondChannel.Object);

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            });
        cache
            .Setup(c => c.GetConnectionAsync(true, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            });

        var attempts = 0;
        var seenChannels = new List<IChannel>();
        var forcedConnections = 0;

        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (ch, _) =>
            {
                attempts++;
                seenChannels.Add(ch);
                if (attempts == 1)
                {
                    throw Interrupted(replyCode);
                }

                return Task.CompletedTask;
            },
            onNewConnectionCallback: _ => forcedConnections++,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal([firstChannel.Object, secondChannel.Object], seenChannels);
        Assert.Equal(2, forcedConnections);
        cache.Verify(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken), Times.Once);
        cache.Verify(c => c.GetConnectionAsync(true, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task
        GetChannelAndDoActionWithRetryAsync_WhenTransientAuthFailure_ForcesSecretRefreshAndNewConnection()
    {
        var firstChannel = new Mock<IChannel>(MockBehavior.Strict);
        var secondChannel = new Mock<IChannel>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper(2);
        var (wrapper, cache, connection, arbiter) = CreateWrapper(retry);

        arbiter
            .Setup(a => a.GetReport(It.IsAny<AuthenticationFailureException>(), 1))
            .Returns(new RabbitMqExceptionArbiterReport
            {
                AlreadyHandled = false,
                IsExpected = true,
                CouldBeTransient = true,
                CouldBeExternallySolvable = true
            });

        connection
            .SetupSequence(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstChannel.Object)
            .ReturnsAsync(secondChannel.Object);

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            });
        cache
            .Setup(c => c.GetConnectionAsync(true, true, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            });

        var attempts = 0;
        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (_, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new AuthenticationFailureException("ACCESS_REFUSED");
                }

                return Task.CompletedTask;
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        cache.Verify(c => c.GetConnectionAsync(true, true, TestContext.Current.CancellationToken), Times.Once);
        arbiter.Verify(a => a.GetReport(It.IsAny<AuthenticationFailureException>(), 1), Times.Once);
    }

    [Fact]
    public async Task GetChannelAndDoActionWithRetryAsync_WhenUncachedConnection_CreatesChannelAndInvokesCallback()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper();
        var (wrapper, cache, connection, _) = CreateWrapper(retry, channel.Object);

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            });

        IConnection? notified = null;
        IChannel? received = null;

        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (ch, _) =>
            {
                received = ch;
                return Task.CompletedTask;
            },
            onNewConnectionCallback: conn => notified = conn,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(channel.Object, received);
        Assert.Same(connection.Object, notified);
        connection.Verify(
            c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task ResetChannel_ClearsCachedChannelSoNextCallCreatesNewOne()
    {
        var firstChannel = new Mock<IChannel>(MockBehavior.Strict);
        var secondChannel = new Mock<IChannel>(MockBehavior.Strict);
        var retry = new ImmediateRetryWrapper();
        var (wrapper, cache, connection, _) = CreateWrapper(retry);

        connection
            .SetupSequence(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstChannel.Object)
            .ReturnsAsync(secondChannel.Object);

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = false,
                Connection = connection.Object
            });

        IChannel? first = null;
        IChannel? second = null;
        await wrapper.GetChannelAndDoActionWithRetryAsync((ch, _) =>
        {
            first = ch;
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        wrapper.ResetChannel();

        cache
            .Setup(c => c.GetConnectionAsync(false, false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new ConnectionCacheResponse
            {
                CachedConnection = true,
                Connection = connection.Object
            });

        await wrapper.GetChannelAndDoActionWithRetryAsync((ch, _) =>
        {
            second = ch;
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(firstChannel.Object, first);
        Assert.Same(secondChannel.Object, second);
        Assert.NotSame(first, second);
    }

    private sealed class ImmediateRetryWrapper(int maxAttempts = 1) : IRabbitMqRetryWrapperService
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<TResult> RunAsync<TResult, TState>(Func<TState, CancellationToken, Task<TResult>> func,
            TState state, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task RunAsync<TState>(Func<TState, CancellationToken, Task> func, TState state,
            CancellationToken cancellationToken = default)
        {
            Exception? last = null;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    await func(state, cancellationToken);
                    return;
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            ExceptionDispatchInfo.Capture(last!).Throw();
        }
    }
}