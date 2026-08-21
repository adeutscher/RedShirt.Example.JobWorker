using Apache.NMS;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using System.Reflection;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqSubscribeJobSourceTests
{
    private const string QueueName = "jobs";

    private static ActiveMqSubscribeJobSource CreateJobSource(
        Mock<IActiveMqConsumerRetryWrapper> consumerRetryWrapper,
        Mock<IJobSubscriberIntakeQueue>? intakeQueue = null,
        Mock<IExecutionEndArbiter>? executionEndArbiter = null,
        Mock<ISleepService>? sleepService = null,
        bool haltOnFailure = true,
        bool treatTransientExceptionAsFailure = false)
    {
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
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

        return new ActiveMqSubscribeJobSource(
            ActiveMqRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            consumerRetryWrapper.Object,
            coreConfiguration.Object,
            (intakeQueue ?? new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict)).Object,
            executionEndArbiter.Object,
            sleepService.Object,
            Options.Create(new ActiveMqConfigurationModel {QueueName = QueueName}),
            NullLogger<ActiveMqSubscribeJobSource>.Instance);
    }

    private static Mock<IActiveMqConsumerRetryWrapper> CreatePassthroughWrapper(IMessageConsumer consumer,
        Action<Action<IConnection>?>? captureOnNewConnection = null)
    {
        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IMessageConsumer, CancellationToken, Task> callback, Action<IConnection>? onNew,
                Action<IMessageConsumer>? _, CancellationToken token) =>
            {
                captureOnNewConnection?.Invoke(onNew);
                return callback(consumer, token);
            });
        return wrapper;
    }

    private static void SetupAsyncListener(Mock<IMessageConsumer> consumer,
        Action<AsyncMessageListener>? captureListener = null)
    {
        consumer
            .SetupAdd(c => c.AsyncListener += It.IsAny<AsyncMessageListener>())
            .Callback<AsyncMessageListener>(handler => captureListener?.Invoke(handler));
        consumer.SetupRemove(c => c.AsyncListener -= It.IsAny<AsyncMessageListener>());
    }

    private static Task InvokeWaitThenStopSubscriberAsync(ActiveMqSubscribeJobSource jobSource,
        CancellationToken cancellationToken)
    {
        var method = typeof(ActiveMqSubscribeJobSource).GetMethod("WaitThenStopSubscriberAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task) method.Invoke(jobSource, [cancellationToken])!;
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenIncompatibleMessage_DoesNotAck()
    {
        var message = new Mock<IMessage>(MockBehavior.Strict);
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        message.Verify(m => m.AcknowledgeAsync(), Times.Never);
        wrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<Action<IMessageConsumer>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Empty)]
    public async Task AcknowledgeAsync_AlwaysAcknowledges(CoreJobResult result)
    {
        var message = new Mock<IMessage>(MockBehavior.Strict);
        message.Setup(m => m.AcknowledgeAsync()).Returns(Task.CompletedTask);

        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new ActiveMqRawJobModel
        {
            Message = message.Object,
            MessageId = "m",
            CreatedAtUtc = DateTime.UtcNow
        }, result, TestContext.Current.CancellationToken);

        message.Verify(m => m.AcknowledgeAsync(), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_ThrowsNotSupportedException()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var jobSource = CreateJobSource(CreatePassthroughWrapper(consumer.Object));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HeartbeatAsync_Completes()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var jobSource = CreateJobSource(CreatePassthroughWrapper(consumer.Object));

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [Fact]
    public void IsSubscriptionSource_IsTrue()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var jobSource = CreateJobSource(CreatePassthroughWrapper(consumer.Object));

        Assert.True(jobSource.IsSubscriptionSource);
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task StartSubscriberAsync_AttachesAsyncListenerAndWaitsForFinished()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
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
        consumer.VerifyAdd(c => c.AsyncListener += It.IsAny<AsyncMessageListener>(), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenConnectionResumes_DoesNotResubscribe()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var connection = new Mock<IConnection>();
        ConnectionResumedListener? resumedHandler = null;
        connection
            .SetupAdd(c => c.ConnectionResumedListener += It.IsAny<ConnectionResumedListener>())
            .Callback<ConnectionResumedListener>(handler => resumedHandler += handler);
        connection.SetupRemove(c => c.ConnectionResumedListener -= It.IsAny<ConnectionResumedListener>());

        var wrapper = CreatePassthroughWrapper(consumer.Object, onNew => onNew?.Invoke(connection.Object));
        var jobSource = CreateJobSource(wrapper);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(resumedHandler);
        resumedHandler!();

        // ActiveMQ client library keeps the listener; we only log on resume.
        consumer.VerifyAdd(c => c.AsyncListener += It.IsAny<AsyncMessageListener>(), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenMessageIdMissing_UsesUnknown()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        AsyncMessageListener? listener = null;
        SetupAsyncListener(consumer, l => listener = l);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        IJobSourceResponse? loaded = null;
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded = response);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var message = new Mock<ITextMessage>(MockBehavior.Strict);
        message.SetupGet(m => m.NMSMessageId).Returns((string) null!);
        message.SetupGet(m => m.Text).Returns("body");

        await listener!(message.Object, TestContext.Current.CancellationToken);

        var job = Assert.IsType<ActiveMqRawJobModel>(Assert.Single(loaded!.Items));
        Assert.Equal("UNKNOWN", job.MessageId);
        Assert.Equal("UNKNOWN", job.IdempotencyId);
        Assert.Equal("body", job.Body);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenNonTransientAndHaltOnFailure_StopsArbiter()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerJobSourceException("permanent")
            {
                CouldBeTransient = false,
                IsHandled = true,
                CouldBeExternallySolvable = false
            });

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);
        executionEndArbiter.Setup(a => a.Stop(It.IsAny<Exception>()));

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter, haltOnFailure: true);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception>()), Times.Once);
        Assert.Empty(consumer.Invocations);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_RemovesAsyncListener()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);

        consumer.VerifyRemove(c => c.AsyncListener -= It.IsAny<AsyncMessageListener>(), Times.Once);
    }
}
