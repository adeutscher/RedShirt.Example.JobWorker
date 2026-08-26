using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Microsoft.Extensions.Logging;
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
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;
using System.Reflection;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqSubscribeJobSourceTests
{
    private const string QueueName = "jobs";

#pragma warning disable S107
    private static ActiveMqSubscribeJobSource CreateJobSource(
        Mock<IActiveMqConsumerRetryWrapper> consumerRetryWrapper,
        Mock<IJobSubscriberIntakeQueue>? intakeQueue = null,
        Mock<IExecutionEndArbiter>? executionEndArbiter = null,
        Mock<ISleepService>? sleepService = null,
        ILogger<ActiveMqSubscribeJobSource>? logger = null,
        IActiveMqSubscribeExceptionArbiter? subscribeExceptionArbiter = null,
        bool haltOnFailure = true,
        bool treatTransientExceptionAsFailure = false)
#pragma warning restore S107
    {
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.SetupGet(c => c.IsHaltOnFailure).Returns(haltOnFailure);
        coreConfiguration.SetupGet(c => c.IsTreatingTransientExceptionAsFailure)
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
            subscribeExceptionArbiter ?? new ActiveMqSubscribeExceptionArbiterService(),
            Options.Create(new ActiveMqConfigurationModel {QueueName = QueueName}),
            logger ?? NullLogger<ActiveMqSubscribeJobSource>.Instance);
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

    private static (Mock<IConnection> Connection, Func<ExceptionListener?> GetExceptionHandler,
        Func<ConnectionResumedListener?> GetResumedHandler) CreateConnectionCapturingListeners()
    {
        var connection = new Mock<IConnection>();
        ExceptionListener? exceptionHandler = null;
        ConnectionResumedListener? resumedHandler = null;

        connection
            .SetupAdd(c => c.ExceptionListener += It.IsAny<ExceptionListener>())
            .Callback<ExceptionListener>(handler => exceptionHandler += handler);
        connection.SetupRemove(c => c.ExceptionListener -= It.IsAny<ExceptionListener>());
        connection
            .SetupAdd(c => c.ConnectionResumedListener += It.IsAny<ConnectionResumedListener>())
            .Callback<ConnectionResumedListener>(handler => resumedHandler += handler);
        connection.SetupRemove(c => c.ConnectionResumedListener -= It.IsAny<ConnectionResumedListener>());

        return (connection, () => exceptionHandler, () => resumedHandler);
    }

    private static Task InvokeWaitThenStopSubscriberAsync(ActiveMqSubscribeJobSource jobSource,
        CancellationToken cancellationToken)
    {
        var method = typeof(ActiveMqSubscribeJobSource).GetMethod("WaitThenStopSubscriberAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task) method.Invoke(jobSource, [cancellationToken])!;
    }

    private static void SetSubscribeLoopRunning(ActiveMqSubscribeJobSource jobSource, bool value)
    {
        var field = typeof(ActiveMqSubscribeJobSource).GetField("_subscribeLoopRunning",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(jobSource, value);
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

    public static TheoryData<Exception> ExceptionListenerReconnectExceptions()
    {
        return
        [
            new EndOfStreamException("peer closed"),
            new NMSSecurityException("bad credentials")
        ];
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
    public async Task StartSubscriberAsync_WhenConnectionResumed_LogsAndDoesNotResubscribe()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var (connection, _, getResumedHandler) = CreateConnectionCapturingListeners();

        var logger = new Mock<ILogger<ActiveMqSubscribeJobSource>>();
        logger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);

        var wrapper = CreatePassthroughWrapper(consumer.Object, onNew => onNew?.Invoke(connection.Object));
        var jobSource = CreateJobSource(wrapper, logger: logger.Object);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var resumedHandler = getResumedHandler();
        Assert.NotNull(resumedHandler);
        resumedHandler!();

        logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("established", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        // ActiveMQ client library keeps the listener; we only log on resume.
        consumer.VerifyAdd(c => c.AsyncListener += It.IsAny<AsyncMessageListener>(), Times.Once);
    }

    [Fact]
    public async Task
        StartSubscriberAsync_WhenExceptionListenerPermanentErrorAndReconnectFailsWithHaltOnFailure_Stops()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var (connection, getExceptionHandler, _) = CreateConnectionCapturingListeners();

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);
        executionEndArbiter
            .Setup(a => a.Stop(It.IsAny<Exception>()))
            .Callback(() => stopped.TrySetResult());

        var subscribeCalls = 0;
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
                subscribeCalls++;
                if (subscribeCalls == 1)
                {
                    onNew?.Invoke(connection.Object);
                    return callback(consumer.Object, token);
                }

                return Task.FromException(new WorkerJobSourceException("still unauthorized")
                {
                    CouldBeTransient = false,
                    IsHandled = true,
                    CouldBeExternallySolvable = false
                });
            });
        wrapper.Setup(w => w.ResetConsumer());

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter, haltOnFailure: true);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var exceptionHandler = getExceptionHandler();
        Assert.NotNull(exceptionHandler);
        exceptionHandler!(new NMSSecurityException("bad credentials"));

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        wrapper.Verify(w => w.ResetConsumer(), Times.Once);
        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenExceptionListenerReportsAccountedTransient_DoesNotWarnUnaccounted()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var (connection, getExceptionHandler, _) = CreateConnectionCapturingListeners();

        var logger = new Mock<ILogger<ActiveMqSubscribeJobSource>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var wrapper = CreatePassthroughWrapper(consumer.Object, onNew => onNew?.Invoke(connection.Object));
        var jobSource = CreateJobSource(wrapper, logger: logger.Object);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var exceptionHandler = getExceptionHandler();
        Assert.NotNull(exceptionHandler);
        exceptionHandler!(new BrokerException());

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Unaccounted-for", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        wrapper.Verify(w => w.ResetConsumer(), Times.Never);
    }

    [Fact]
    public async Task
        StartSubscriberAsync_WhenExceptionListenerReportsProblemWhileSubscribeLoopRunning_DoesNotReconnect()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var (connection, getExceptionHandler, _) = CreateConnectionCapturingListeners();
        var wrapper = CreatePassthroughWrapper(consumer.Object, onNew => onNew?.Invoke(connection.Object));
        wrapper.Setup(w => w.ResetConsumer());

        var jobSource = CreateJobSource(wrapper);
        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        SetSubscribeLoopRunning(jobSource, true);

        var exceptionHandler = getExceptionHandler();
        Assert.NotNull(exceptionHandler);
        exceptionHandler!(new EndOfStreamException("peer closed"));

        // Early return before ResetConsumer / Task.Run when a subscribe loop is already in flight.
        wrapper.Verify(w => w.ResetConsumer(), Times.Never);
        wrapper.Verify(w => w.GetChannelAndDoActionWithRetryAsync(
            It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
            It.IsAny<Action<IConnection>?>(),
            It.IsAny<Action<IMessageConsumer>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(ExceptionListenerReconnectExceptions))]
    public async Task StartSubscriberAsync_WhenExceptionListenerReportsReconnectOrStopWorthy_Reconnects(
        Exception exception)
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var (connection, getExceptionHandler, _) = CreateConnectionCapturingListeners();

        var logger = new Mock<ILogger<ActiveMqSubscribeJobSource>>();
        logger.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);

        var subscribeCalls = 0;
        var reSubscribeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
                subscribeCalls++;
                if (subscribeCalls == 1)
                {
                    onNew?.Invoke(connection.Object);
                }
                else
                {
                    reSubscribeStarted.TrySetResult();
                }

                return callback(consumer.Object, token);
            });
        wrapper.Setup(w => w.ResetConsumer());

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter, logger: logger.Object);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var exceptionHandler = getExceptionHandler();
        Assert.NotNull(exceptionHandler);
        exceptionHandler!(exception);

        await reSubscribeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("ExceptionListener problem", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        wrapper.Verify(w => w.ResetConsumer(), Times.Once);
        Assert.True(subscribeCalls >= 2);
        // Reconnect succeeded; ExceptionListener itself does not call Stop.
        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenExceptionListenerReportsUnaccountedException_LogsWarning()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var (connection, getExceptionHandler, _) = CreateConnectionCapturingListeners();

        var logger = new Mock<ILogger<ActiveMqSubscribeJobSource>>();
        logger.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);

        var wrapper = CreatePassthroughWrapper(consumer.Object, onNew => onNew?.Invoke(connection.Object));
        var jobSource = CreateJobSource(wrapper, logger: logger.Object);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var exceptionHandler = getExceptionHandler();
        Assert.NotNull(exceptionHandler);
        exceptionHandler!(new Exception("mystery"));

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Unaccounted-for", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
    public async Task StartSubscriberAsync_WhenOperationCanceled_ExitsWithoutStopping()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IMessageConsumer, CancellationToken, Task> _, Action<IConnection>? __,
                    Action<IMessageConsumer>? ___, CancellationToken token) =>
                Task.FromException(new OperationCanceledException(token)));

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(cts.Token);

        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenPermanentFailureAndNotHaltOnFailure_RetriesUntilSuccess()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        var attempts = 0;
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IMessageConsumer, CancellationToken, Task> callback, Action<IConnection>? _,
                Action<IMessageConsumer>? __, CancellationToken token) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromException(new InvalidOperationException("permanent"));
                }

                return callback(consumer.Object, token);
            });

        var jobSource = CreateJobSource(wrapper, haltOnFailure: false);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        consumer.VerifyAdd(c => c.AsyncListener += It.IsAny<AsyncMessageListener>(), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenReceiveFires_LoadsIntakeQueue()
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
        message.SetupGet(m => m.NMSMessageId).Returns("msg-1");
        message.SetupGet(m => m.Text).Returns("payload");

        await listener!(message.Object, TestContext.Current.CancellationToken);

        var job = Assert.IsType<ActiveMqRawJobModel>(Assert.Single(loaded!.Items));
        Assert.Equal("msg-1", job.MessageId);
        Assert.Equal("msg-1", job.IdempotencyId);
        Assert.Equal("payload", job.Body);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenReceiveHandlerThrows_FaultsTask()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        AsyncMessageListener? listener = null;
        SetupAsyncListener(consumer, l => listener = l);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Throws(new InvalidOperationException("intake failed"));

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var message = new Mock<ITextMessage>(MockBehavior.Strict);
        message.SetupGet(m => m.NMSMessageId).Returns("msg-1");

        var faulted = listener!(message.Object, TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => faulted);
        Assert.Equal("intake failed", exception.Message);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenTransientFailure_RetriesUntilSuccess()
    {
        var consumer = new Mock<IMessageConsumer>(MockBehavior.Strict);
        SetupAsyncListener(consumer);

        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        var attempts = 0;
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<IMessageConsumer, CancellationToken, Task> callback, Action<IConnection>? _,
                Action<IMessageConsumer>? __, CancellationToken token) =>
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

                return callback(consumer.Object, token);
            });

        var jobSource = CreateJobSource(wrapper);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        consumer.VerifyAdd(c => c.AsyncListener += It.IsAny<AsyncMessageListener>(), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenTransientTreatedAsFailureAndHaltOnFailure_Stops()
    {
        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkerJobSourceException("transient")
            {
                IsHandled = true,
                CouldBeTransient = true,
                CouldBeExternallySolvable = true
            });

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource().Task);
        executionEndArbiter.Setup(a => a.Stop(It.IsAny<Exception>()));

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter, haltOnFailure: true,
            treatTransientExceptionAsFailure: true);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception>()), Times.Once);
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

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenUnsubscribeThrows_IsSwallowed()
    {
        var wrapper = new Mock<IActiveMqConsumerRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetChannelAndDoActionWithRetryAsync(
                It.IsAny<Func<IMessageConsumer, CancellationToken, Task>>(),
                It.IsAny<Action<IConnection>?>(),
                It.IsAny<Action<IMessageConsumer>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unsubscribe failed"));

        var logger = new Mock<ILogger<ActiveMqSubscribeJobSource>>();
        logger.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(true);

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter, logger: logger.Object);

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Could not unsubscribe", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}