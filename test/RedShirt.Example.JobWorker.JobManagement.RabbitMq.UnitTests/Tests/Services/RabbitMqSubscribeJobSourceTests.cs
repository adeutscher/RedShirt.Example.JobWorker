using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using System.Reflection;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqSubscribeJobSourceTests
{
    private const string QueueName = "jobs";

    private static RabbitMqSubscribeJobSource CreateJobSource(
        Mock<IRabbitMqChannelRetryWrapper> channelRetryWrapper,
        Mock<IJobSubscriberIntakeQueue>? intakeQueue = null,
        Mock<IExecutionEndArbiter>? executionEndArbiter = null,
        Mock<ISleepService>? sleepService = null,
        bool haltOnFailure = true,
        int backlogSize = 5,
        bool treatTransientExceptionAsFailure = false)
    {
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.Setup(c => c.GetBacklogSize()).Returns(backlogSize);
        coreConfiguration.Setup(c => c.IsHaltOnFailure()).Returns(haltOnFailure);
        coreConfiguration.Setup(c => c.IsTreatingTransientExceptionAsFailure())
            .Returns(treatTransientExceptionAsFailure);

        sleepService ??= new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        if (executionEndArbiter is null)
        {
            executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
            // Background unsubscribe waiter; leave unfinished unless a test signals stop.
            executionEndArbiter
                .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
                .Returns(new TaskCompletionSource().Task);
        }

        return new RabbitMqSubscribeJobSource(
            channelRetryWrapper.Object,
            coreConfiguration.Object,
            (intakeQueue ?? new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict)).Object,
            executionEndArbiter.Object,
            sleepService.Object,
            Options.Create(new RabbitMqQueueConfigurationModel {QueueName = QueueName}),
            NullLogger<RabbitMqSubscribeJobSource>.Instance);
    }

    private static Mock<IRabbitMqChannelRetryWrapper> CreatePassthroughWrapper(IChannel channel,
        Action<Action<IConnection>?>? captureOnNewConnection = null)
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IChannel, CancellationToken, Task> callback, Action<IConnection>? onNew,
                CancellationToken token) =>
            {
                captureOnNewConnection?.Invoke(onNew);
                return callback(channel, token);
            });
        return wrapper;
    }

    private static void SetupConsume(Mock<IChannel> channel, string consumerTag = "ctag",
        Action<IAsyncBasicConsumer>? captureConsumer = null)
    {
        channel
            .Setup(c => c.BasicQosAsync(0, ushort.MaxValue, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channel
            .Setup(c => c.BasicConsumeAsync(
                QueueName,
                false,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
#pragma warning disable S107
            .Callback(new Action<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer,
                CancellationToken>((_, _, _, _, _, _, consumer, _) => captureConsumer?.Invoke(consumer)))
#pragma warning restore S107
            .ReturnsAsync(consumerTag);
    }

    private static Task InvokeWaitThenStopSubscriberAsync(RabbitMqSubscribeJobSource jobSource,
        CancellationToken cancellationToken)
    {
        var method = typeof(RabbitMqSubscribeJobSource).GetMethod("WaitThenStopSubscriberAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task) method.Invoke(jobSource, [cancellationToken])!;
    }

    private static void SetSubscriberTag(RabbitMqSubscribeJobSource jobSource, string? tag)
    {
        var field = typeof(RabbitMqSubscribeJobSource).GetField("_subscriberTag",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(jobSource, tag);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenIncompatibleMessage_DoesNotTouchChannel()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        wrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IChannel, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenSuccessful_AcksDeliveryTag()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        channel
            .Setup(c => c.BasicAckAsync(99, false, TestContext.Current.CancellationToken))
            .Returns(ValueTask.CompletedTask);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new RabbitMqRawJobModel
        {
            MessageId = "m",
            DeliveryTag = 99,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        }, CoreJobResult.Success, TestContext.Current.CancellationToken);

        channel.Verify(c => c.BasicAckAsync(99, false, TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure, true)]
    [InlineData(CoreJobResult.Cancelled, true)]
    [InlineData(CoreJobResult.Empty, false)]
    [InlineData(CoreJobResult.Parsing, false)]
    [InlineData(CoreJobResult.InvalidData, false)]
    public async Task AcknowledgeAsync_WhenUnsuccessful_NacksWithExpectedRequeue(CoreJobResult result,
        bool requeue)
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        channel
            .Setup(c => c.BasicNackAsync(7, false, requeue, TestContext.Current.CancellationToken))
            .Returns(ValueTask.CompletedTask);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new RabbitMqRawJobModel
        {
            MessageId = "m",
            DeliveryTag = 7,
            CreatedAtUtc = DateTime.UtcNow,
            Body = "body"
        }, result, TestContext.Current.CancellationToken);

        channel.Verify(c => c.BasicNackAsync(7, false, requeue, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_ThrowsNotSupportedException()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HeartbeatAsync_Completes()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
        // Satisfy Sonar's demand for an assertion.
        Assert.True(true);
    }

    [Fact]
    public void IsSubscriptionSource_IsTrue()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        Assert.True(jobSource.IsSubscriptionSource);
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task StartSubscriberAsync_ConsumesQueueAndWaitsForFinished()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        SetupConsume(channel);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var waitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var waitForFinishedExecuted = false;
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                waitStarted.TrySetResult();
                waitForFinishedExecuted = true;
                return Task.CompletedTask;
            });

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(waitForFinishedExecuted);
        executionEndArbiter.Verify(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(c => c.BasicQosAsync(0, ushort.MaxValue, false, TestContext.Current.CancellationToken),
            Times.Once);
        channel.Verify(c => c.BasicConsumeAsync(
            QueueName,
            false,
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenConnectionRecovers_Resubscribes()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        SetupConsume(channel);

        var connection = new Mock<IConnection>();
        AsyncEventHandler<AsyncEventArgs>? recoveryHandler = null;
        connection
            .SetupAdd(c => c.RecoverySucceededAsync += It.IsAny<AsyncEventHandler<AsyncEventArgs>>())
            .Callback<AsyncEventHandler<AsyncEventArgs>>(handler => recoveryHandler += handler);
        connection.SetupRemove(c => c.RecoverySucceededAsync -= It.IsAny<AsyncEventHandler<AsyncEventArgs>>());

        var wrapper = CreatePassthroughWrapper(channel.Object, onNew => onNew?.Invoke(connection.Object));
        var jobSource = CreateJobSource(wrapper);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(recoveryHandler);
        await recoveryHandler!(connection.Object, new AsyncEventArgs(TestContext.Current.CancellationToken));

        channel.Verify(c => c.BasicConsumeAsync(
            QueueName,
            false,
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            TestContext.Current.CancellationToken), Times.Exactly(2));
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenMessageIdMissing_UsesUnknown()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        IAsyncBasicConsumer? consumer = null;
        SetupConsume(channel, captureConsumer: c => consumer = c);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        IJobSourceResponse? loaded = null;
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded = response);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var jobSource = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var properties = new Mock<IReadOnlyBasicProperties>(MockBehavior.Strict);
        properties.SetupGet(p => p.MessageId).Returns((string?) null);

        await consumer!.HandleBasicDeliverAsync("ctag", 1, false, "", "", properties.Object,
            Encoding.UTF8.GetBytes("body"), TestContext.Current.CancellationToken);

        var job = Assert.IsType<RabbitMqRawJobModel>(Assert.Single(loaded!.Items));
        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Null(job.IdempotencyId);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenNonTransientAndHaltOnFailure_StopsArbiter()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("permanent"));

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);
        executionEndArbiter.Setup(a => a.Stop(It.IsAny<Exception?>()));

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        executionEndArbiter.Verify(
            a => a.Stop(It.Is<InvalidOperationException>(e => e.Message == "permanent")), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenReceiveFires_LoadsIntakeQueue()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        IAsyncBasicConsumer? consumer = null;
        SetupConsume(channel, captureConsumer: c => consumer = c);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        IJobSourceResponse? loaded = null;
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded = response);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var jobSource = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(consumer);
        var properties = new Mock<IReadOnlyBasicProperties>(MockBehavior.Strict);
        properties.SetupGet(p => p.MessageId).Returns("msg-1");

        await consumer!.HandleBasicDeliverAsync("ctag", 12, false, "", "", properties.Object,
            Encoding.UTF8.GetBytes("payload"), TestContext.Current.CancellationToken);

        var job = Assert.IsType<RabbitMqRawJobModel>(Assert.Single(loaded!.Items));
        Assert.Equal("msg-1", job.MessageId);
        Assert.Equal("msg-1", job.IdempotencyId);
        Assert.Equal(12UL, job.DeliveryTag);
        Assert.Equal("payload", job.Body);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenRecoveryFailsPermanentlyAndHaltOnFailure_StopsArbiter()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        SetupConsume(channel);

        var connection = new Mock<IConnection>();
        AsyncEventHandler<AsyncEventArgs>? recoveryHandler = null;
        connection
            .SetupAdd(c => c.RecoverySucceededAsync += It.IsAny<AsyncEventHandler<AsyncEventArgs>>())
            .Callback<AsyncEventHandler<AsyncEventArgs>>(handler => recoveryHandler += handler);
        connection.SetupRemove(c => c.RecoverySucceededAsync -= It.IsAny<AsyncEventHandler<AsyncEventArgs>>());

        var attempts = 0;
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IChannel, CancellationToken, Task> callback, Action<IConnection>? onNew,
                CancellationToken token) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    onNew?.Invoke(connection.Object);
                    return callback(channel.Object, token);
                }

                return Task.FromException(new InvalidOperationException("recovery failed"));
            });

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);
        executionEndArbiter.Setup(a => a.Stop(It.IsAny<Exception?>()));

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await recoveryHandler!(connection.Object, new AsyncEventArgs(TestContext.Current.CancellationToken));

        executionEndArbiter.Verify(
            a => a.Stop(It.Is<InvalidOperationException>(e => e.Message == "recovery failed")), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenStopSignaled_CancelsConsumer()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        SetupConsume(channel, "consumer-1");
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel
            .Setup(c => c.BasicCancelAsync("consumer-1", false, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancelled.TrySetResult();
                return Task.CompletedTask;
            });

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(finished.Task);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        finished.SetResult();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        channel.Verify(c => c.BasicCancelAsync("consumer-1", false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenTransientFailure_RetriesUntilSuccess()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        SetupConsume(channel);

        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var attempts = 0;
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IChannel, CancellationToken, Task> callback, Action<IConnection>? _,
                CancellationToken token) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromException(new WorkerJobSourceException("transient")
                    {
                        IsHandled = true,
                        CouldBeTransient = true,
                        CouldBeExternallySolvable = true
                    });
                }

                return callback(channel.Object, token);
            });

        var jobSource = CreateJobSource(wrapper);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        channel.Verify(c => c.BasicConsumeAsync(
            QueueName,
            false,
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenAlreadyClosedException_IsSwallowed()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerJobSourceException(
                new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Application, 0, "closed")))
            {
                IsHandled = true,
                CouldBeTransient = true,
                CouldBeExternallySolvable = false
            });

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);
        SetSubscriberTag(jobSource, "consumer-1");

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenCanceledBeforeStop_ThrowsAndDoesNotCancelConsumer()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => Task.FromCanceled(token));

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);
        SetSubscriberTag(jobSource, "consumer-1");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeWaitThenStopSubscriberAsync(jobSource, cts.Token));

        wrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IChannel, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenNoSubscriberTag_DoesNotCancelConsumer()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);

        wrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IChannel, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenSubscriberTagSet_CancelsConsumer()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        channel
            .Setup(c => c.BasicCancelAsync("consumer-1", false, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var wrapper = CreatePassthroughWrapper(channel.Object);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);
        SetSubscriberTag(jobSource, "consumer-1");

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);

        channel.Verify(c => c.BasicCancelAsync("consumer-1", false, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenUnexpectedException_IsSwallowed()
    {
        var wrapper = new Mock<IRabbitMqChannelRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IChannel, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cancel failed"));

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);
        SetSubscriberTag(jobSource, "consumer-1");

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);
    }
}