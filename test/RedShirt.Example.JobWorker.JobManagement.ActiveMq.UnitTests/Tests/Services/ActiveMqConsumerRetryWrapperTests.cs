using Apache.NMS;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;
using System.Runtime.ExceptionServices;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqConsumerRetryWrapperTests
{
    private static (Mock<IActiveMqConnectionFactory> Factory, Mock<IConnection> Connection, Mock<ISession> Session,
        Mock<IQueue> Queue)
        CreateInfrastructure(string queueName, IMessageConsumer? consumer = null)
    {
        var queue = new Mock<IQueue>(MockBehavior.Strict);
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(s => s.GetQueueAsync(queueName)).ReturnsAsync(queue.Object);
        if (consumer is not null)
        {
            session.Setup(s => s.CreateConsumerAsync(queue.Object)).ReturnsAsync(consumer);
        }

        var connection = new Mock<IConnection>(MockBehavior.Strict);
        connection.Setup(c => c.StartAsync()).Returns(Task.CompletedTask);
        connection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(session.Object);

        var factory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        factory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        return (factory, connection, session, queue);
    }

    private static ActiveMqConsumerRetryWrapper CreateWrapper(
        IActiveMqRetryWrapperService retry,
        IActiveMqConnectionFactory factory,
        string queueName)
    {
        return new ActiveMqConsumerRetryWrapper(
            factory,
            retry,
            Options.Create(new ActiveMqConfigurationModel
            {
                QueueName = queueName
            }));
    }

    [Fact]
    public async Task GetChannelAndDoActionWithRetryAsync_WhenCachedConsumer_ReusesAndSkipsCallbacks()
    {
        var queueName = Guid.NewGuid().ToString();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var (factory, connection, session, _) = CreateInfrastructure(queueName, consumer.Object);
        var wrapper = CreateWrapper(new ImmediateRetryWrapper(), factory.Object, queueName);

        var newConnectionCalls = 0;
        var newConsumerCalls = 0;

        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (_, _) => Task.CompletedTask,
            _ => newConnectionCalls++,
            _ => newConsumerCalls++,
            TestContext.Current.CancellationToken);
        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (_, _) => Task.CompletedTask,
            _ => newConnectionCalls++,
            _ => newConsumerCalls++,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, newConnectionCalls);
        Assert.Equal(1, newConsumerCalls);
        factory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Once);
        connection.Verify(c => c.StartAsync(), Times.Once);
        session.Verify(s => s.CreateConsumerAsync(It.IsAny<IQueue>()), Times.Once);
    }

    [Fact]
    public async Task GetChannelAndDoActionWithRetryAsync_WhenCallbackFails_ResetsConsumerAndRecreates()
    {
        var queueName = Guid.NewGuid().ToString();
        var firstConsumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var secondConsumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var queue = new Mock<IQueue>(MockBehavior.Strict);
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(s => s.GetQueueAsync(queueName)).ReturnsAsync(queue.Object);
        session.SetupSequence(s => s.CreateConsumerAsync(queue.Object))
            .ReturnsAsync(firstConsumer.Object)
            .ReturnsAsync(secondConsumer.Object);

        var connection = new Mock<IConnection>(MockBehavior.Strict);
        connection.Setup(c => c.StartAsync()).Returns(Task.CompletedTask);
        connection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(session.Object);

        var factory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        factory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var wrapper = CreateWrapper(new ImmediateRetryWrapper(2), factory.Object, queueName);

        var attempts = 0;
        var seenConsumers = new List<IMessageConsumer>();
        var newConsumerCalls = 0;

        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (c, _) =>
            {
                attempts++;
                seenConsumers.Add(c);
                if (attempts == 1)
                {
                    throw new TimeoutException("transient");
                }

                return Task.CompletedTask;
            },
            onNewMessageConsumerCallback: _ => newConsumerCalls++,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal([firstConsumer.Object, secondConsumer.Object], seenConsumers);
        Assert.Equal(2, newConsumerCalls);
        factory.Verify(f => f.GetConnectionAsync(TestContext.Current.CancellationToken), Times.Exactly(2));
        session.Verify(s => s.CreateConsumerAsync(queue.Object), Times.Exactly(2));
    }

    [Fact]
    public async Task GetChannelAndDoActionWithRetryAsync_WhenQueueMissing_ThrowsCouldNotLoadQueueException()
    {
        var queueName = Guid.NewGuid().ToString();
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(s => s.GetQueueAsync(queueName)).ReturnsAsync((IQueue?) null);

        var connection = new Mock<IConnection>(MockBehavior.Strict);
        connection.Setup(c => c.StartAsync()).Returns(Task.CompletedTask);
        connection.Setup(c => c.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge))
            .ReturnsAsync(session.Object);

        var factory = new Mock<IActiveMqConnectionFactory>(MockBehavior.Strict);
        factory.Setup(f => f.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var wrapper = CreateWrapper(new ImmediateRetryWrapper(), factory.Object, queueName);

        await Assert.ThrowsAsync<CouldNotLoadQueueException>(() =>
            wrapper.GetChannelAndDoActionWithRetryAsync((_, _) => Task.CompletedTask,
                cancellationToken: TestContext.Current.CancellationToken));

        session.Verify(s => s.CreateConsumerAsync(It.IsAny<IDestination>()), Times.Never);
    }

    [Fact]
    public async Task GetChannelAndDoActionWithRetryAsync_WhenUncached_CreatesConsumerAndInvokesCallbacks()
    {
        var queueName = Guid.NewGuid().ToString();
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var (factory, connection, _, _) = CreateInfrastructure(queueName, consumer.Object);
        var wrapper = CreateWrapper(new ImmediateRetryWrapper(), factory.Object, queueName);

        IConnection? notifiedConnection = null;
        IMessageConsumer? notifiedConsumer = null;
        IMessageConsumer? received = null;

        await wrapper.GetChannelAndDoActionWithRetryAsync(
            (c, _) =>
            {
                received = c;
                return Task.CompletedTask;
            },
            conn => notifiedConnection = conn,
            c => notifiedConsumer = c,
            TestContext.Current.CancellationToken);

        Assert.Same(consumer.Object, received);
        Assert.Same(connection.Object, notifiedConnection);
        Assert.Same(consumer.Object, notifiedConsumer);
        connection.Verify(c => c.StartAsync(), Times.Once);
    }

    private sealed class ImmediateRetryWrapper(int maxAttempts = 1) : IActiveMqRetryWrapperService
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
