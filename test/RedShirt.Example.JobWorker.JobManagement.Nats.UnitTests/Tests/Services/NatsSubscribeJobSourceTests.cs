using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services.Resilience;
using System.Runtime.CompilerServices;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Services;

public class NatsSubscribeJobSourceTests
{
    private const string StreamName = "jobs-stream";

    private static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<INatsJSMsg<NatsMemoryOwner<byte>>> WaitUntilCancelledAsync(
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when the test ends or cancels consumption.
        }

        yield break;
    }

    private static (NatsSubscribeJobSource JobSource, Mock<IExecutionEndArbiter> ExecutionEndArbiter,
        Action<Exception?>? OnStopCallback) CreateJobSource(
            Mock<INatsConnectionRetryWrapper> connectionRetryWrapper,
            Mock<IJobSubscriberIntakeQueue>? intakeQueue = null,
            Mock<IExecutionEndArbiter>? executionEndArbiter = null,
            Mock<ISleepService>? sleepService = null,
            bool haltOnFailure = true,
            int fetchCount = 5,
            bool treatTransientExceptionAsFailure = false,
            int visibilityTimeoutSeconds = 20)
    {
        var coreConfiguration = new Mock<ICoreConfigurationService>(MockBehavior.Strict);
        coreConfiguration.SetupGet(c => c.FetchCount).Returns(fetchCount);
        coreConfiguration.SetupGet(c => c.IsHaltOnFailure).Returns(haltOnFailure);
        coreConfiguration.SetupGet(c => c.IsTreatingTransientExceptionAsFailure)
            .Returns(treatTransientExceptionAsFailure);

        sleepService ??= new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        executionEndArbiter ??= new Mock<IExecutionEndArbiter>(MockBehavior.Strict);

        Action<Exception?>? onStopCallback = null;
        executionEndArbiter
            .Setup(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()))
            .Callback<Action<Exception?>>(callback => onStopCallback = callback);

        var jobSource = new NatsSubscribeJobSource(
            connectionRetryWrapper.Object,
            NatsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            coreConfiguration.Object,
            (intakeQueue ?? new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict)).Object,
            executionEndArbiter.Object,
            sleepService.Object,
            Options.Create(new NatsStreamConfigurationModel
            {
                StreamName = StreamName,
                ConsumerName = "worker"
            }),
            Options.Create(new NatsStreamTimeoutConfigurationModel
            {
                VisibilityTimeoutSeconds = visibilityTimeoutSeconds
            }),
            NullLogger<NatsSubscribeJobSource>.Instance);

        return (jobSource, executionEndArbiter, onStopCallback);
    }

    private static Mock<INatsConnectionRetryWrapper> CreatePassthroughWrapper(INatsJSConsumer consumer)
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<INatsJSConsumer, CancellationToken, Task> callback, bool _, Action<INatsConnection>? __,
                CancellationToken token) => callback(consumer, token));
        return wrapper;
    }

    private static void SetupBlockingConsume(Mock<INatsJSConsumer> consumer,
        TaskCompletionSource? consumeStarted = null)
    {
        consumer
            .Setup(c => c.ConsumeAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSConsumeOpts>(),
                It.IsAny<CancellationToken>()))
            .Returns((INatsDeserialize<NatsMemoryOwner<byte>> _, NatsJSConsumeOpts __, CancellationToken ct) =>
            {
                consumeStarted?.TrySetResult();
                return WaitUntilCancelledAsync(ct);
            });
    }

    private static void SetupConsumeMessages(Mock<INatsJSConsumer> consumer,
        params INatsJSMsg<NatsMemoryOwner<byte>>[] messages)
    {
        consumer
            .Setup(c => c.ConsumeAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSConsumeOpts>(),
                It.IsAny<CancellationToken>()))
            .Returns((INatsDeserialize<NatsMemoryOwner<byte>> _, NatsJSConsumeOpts __, CancellationToken _) =>
                AsAsyncEnumerable(messages));
    }

    private static INatsJSMsg<NatsMemoryOwner<byte>> CreateMessage(string body, ulong streamSequence = 42)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var owner = NatsMemoryOwner<byte>.Allocate(bytes.Length);
        bytes.AsSpan().CopyTo(owner.Span);

        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>(MockBehavior.Strict);
        message.Setup(m => m.Data).Returns(owner);
        message.Setup(m => m.Metadata).Returns(new NatsJSMsgMetadata(
            new NatsJSSequencePair(streamSequence, streamSequence),
            1,
            0,
            DateTimeOffset.UtcNow,
            StreamName,
            "worker",
            string.Empty));
        message
            .Setup(m => m.AckAsync(It.IsAny<AckOpts?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        return message.Object;
    }

    private static WorkerJobSourceException CreateTransientException(string message = "transient")
    {
        return new WorkerJobSourceException(message)
        {
            IsHandled = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static async IAsyncEnumerable<INatsJSMsg<NatsMemoryOwner<byte>>> WaitAndSignalAsync(
        [EnumeratorCancellation]
        CancellationToken cancellationToken,
        TaskCompletionSource ended)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ended.TrySetResult();
        }

        yield break;
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenIncompatibleMessage_DoesNotAckOrTouchConnection()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        wrapper.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task AcknowledgeAsync_WhenNatsJob_AcksRegardlessOfResult(CoreJobResult result)
    {
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>(MockBehavior.Strict);
        message
            .Setup(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken))
            .Returns(ValueTask.CompletedTask);

        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new NatsRawJobModel
        {
            Message = message.Object,
            MessageId = "m",
            CreatedAtUtc = DateTime.UtcNow
        }, result, TestContext.Current.CancellationToken);

        message.Verify(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public void Constructor_RegistersOnStopCallback()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (_, executionEndArbiter, onStopCallback) = CreateJobSource(wrapper);

        Assert.NotNull(onStopCallback);
        executionEndArbiter.Verify(a => a.AddOnStopCallback(It.IsAny<Action<Exception?>>()), Times.Once);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        jobSource.Dispose();
        jobSource.Dispose();
    }

    [Fact]
    public void Dispose_ThenOnStopCallback_DoesNotThrow()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, onStopCallback) = CreateJobSource(wrapper);

        jobSource.Dispose();
        onStopCallback!(new InvalidOperationException("already disposed"));
    }

    [Fact]
    public async Task GetJobsAsync_ThrowsNotSupportedException()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HeartbeatAsync_WhenIncompatibleMessage_DoesNothing()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HeartbeatAsync_WhenNatsJob_SendsAckProgress()
    {
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>(MockBehavior.Strict);
        message
            .Setup(m => m.AckProgressAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken))
            .Returns(ValueTask.CompletedTask);

        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        await jobSource.HeartbeatAsync(new NatsRawJobModel
        {
            Message = message.Object,
            MessageId = "m",
            CreatedAtUtc = DateTime.UtcNow
        }, TestContext.Current.CancellationToken);

        message.Verify(m => m.AckProgressAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public void IsSubscriptionSource_IsTrue_AndHeartbeatIntervalUsesVisibilityTimeout()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper, visibilityTimeoutSeconds: 40);

        Assert.True(jobSource.IsSubscriptionSource);
        Assert.Equal(30, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    public async Task StartSubscriberAsync_ConsumesWithEffectiveFetchCount(int fetchCount, int expectedMaxMsgs)
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupBlockingConsume(consumer, consumeStarted);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, _) = CreateJobSource(wrapper, fetchCount: fetchCount);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await jobSource.StartSubscriberAsync(cts.Token);

        await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        consumer.Verify(
            c => c.ConsumeAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.Is<NatsJSConsumeOpts>(o => o.MaxMsgs == expectedMaxMsgs),
                It.IsAny<CancellationToken>()),
            Times.Once);
        wrapper.Verify(
            w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                false,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task StartSubscriberAsync_ReturnsImmediatelyWithoutAwaitingConsume()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        SetupBlockingConsume(consumer);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(
            jobSource.StartSubscriberAsync(cts.Token),
            Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.True(completed.IsCompletedSuccessfully);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenAlreadyRunning_DoesNotStartSecondLoop()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupBlockingConsume(consumer, consumeStarted);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await jobSource.StartSubscriberAsync(cts.Token);
        await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await jobSource.StartSubscriberAsync(cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        wrapper.Verify(
            w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenCancelled_DoesNotStopArbiter()
    {
        var loopFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<INatsJSConsumer, CancellationToken, Task>, bool, Action<INatsConnection>?,
                CancellationToken>((_, _, _, _) =>
            {
                loopFinished.TrySetResult();
                return Task.FromException(new OperationCanceledException());
            });

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await loopFinished.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception?>()), Times.Never);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenDisposedBeforeStart_DoesNotConsume()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper);

        jobSource.Dispose();
        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        wrapper.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenExecutionStops_CancelsConsume()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumeEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer
            .Setup(c => c.ConsumeAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSConsumeOpts>(),
                It.IsAny<CancellationToken>()))
            .Returns((INatsDeserialize<NatsMemoryOwner<byte>> _, NatsJSConsumeOpts __, CancellationToken ct) =>
            {
                consumeStarted.TrySetResult();
                return WaitAndSignalAsync(ct, consumeEnded);
            });

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, onStopCallback) = CreateJobSource(wrapper);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        onStopCallback!(null);
        await consumeEnded.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenMessageReceived_LoadsIntakeQueue()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var message = CreateMessage("payload", 99);
        SetupConsumeMessages(consumer, message);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        var loaded = new TaskCompletionSource<IJobSourceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded.TrySetResult(response));

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, _) = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var response = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var job = Assert.IsType<NatsRawJobModel>(Assert.Single(response.Items));
        Assert.Equal("99", job.MessageId);
        Assert.Equal("99", job.IdempotencyId);
        Assert.Equal("payload", job.Body);
        Assert.Same(message, job.Message);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenMetadataMissing_UsesUnknownMessageId()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>(MockBehavior.Strict);
        message.Setup(m => m.Data).Returns(NatsMemoryOwner<byte>.Allocate(0));
        message.Setup(m => m.Metadata).Returns((NatsJSMsgMetadata?) null);
        SetupConsumeMessages(consumer, message.Object);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        var loaded = new TaskCompletionSource<IJobSourceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded.TrySetResult(response));

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, _) = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var response = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal("UNKNOWN", Assert.Single(response.Items).MessageId);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenMultipleMessages_LoadsEachSeparately()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        SetupConsumeMessages(consumer, CreateMessage("a", 1), CreateMessage("b", 2));

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        var loads = new List<IJobSourceResponse>();
        var secondLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response =>
            {
                loads.Add(response);
                if (loads.Count >= 2)
                {
                    secondLoad.TrySetResult();
                }
            });

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var (jobSource, _, _) = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await secondLoad.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(2, loads.Count);
        Assert.All(loads, response => Assert.Single(response.Items));
        Assert.Equal(["1", "2"], loads.Select(r => r.Items[0].MessageId).ToArray());
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenNonTransientAndHaltOnFailure_StopsArbiter()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("permanent"));

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.Stop(It.IsAny<Exception?>()))
            .Callback(() => stopped.TrySetResult());

        var (jobSource, _, _) = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        executionEndArbiter.Verify(
            a => a.Stop(It.Is<InvalidOperationException>(e => e.Message == "permanent")), Times.Once);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenNonTransientAndNotHaltOnFailure_Retries()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupBlockingConsume(consumer, consumeStarted);

        var attempts = 0;
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<INatsJSConsumer, CancellationToken, Task> callback, bool _, Action<INatsConnection>? __,
                CancellationToken token) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromException(new InvalidOperationException("permanent"));
                }

                return callback(consumer.Object, token);
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var (jobSource, _, _) = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter,
            sleepService: sleepService, haltOnFailure: false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await jobSource.StartSubscriberAsync(cts.Token);
        await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        executionEndArbiter.Verify(a => a.Stop(It.IsAny<Exception?>()), Times.Never);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()), Times.Once);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenTransientFailure_RetriesUntilSuccess()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupBlockingConsume(consumer, consumeStarted);

        var attempts = 0;
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<INatsJSConsumer, CancellationToken, Task> callback, bool _, Action<INatsConnection>? __,
                CancellationToken token) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromException(CreateTransientException());
                }

                return callback(consumer.Object, token);
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (jobSource, _, _) = CreateJobSource(wrapper, sleepService: sleepService);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await jobSource.StartSubscriberAsync(cts.Token);
        await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(1), It.IsAny<CancellationToken>()), Times.Once);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenTransientTreatedAsFailureAndHaltOnFailure_StopsArbiter()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper
            .Setup(w => w.GetConsumerAndDoActionWithRetryAsync(
                It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<Action<INatsConnection>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateTransientException("treated as failure"));

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.Stop(It.IsAny<Exception?>()))
            .Callback(() => stopped.TrySetResult());

        var (jobSource, _, _) = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter,
            treatTransientExceptionAsFailure: true);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        executionEndArbiter.Verify(
            a => a.Stop(It.Is<WorkerJobSourceException>(e => e.Message == "treated as failure")), Times.Once);
    }
}