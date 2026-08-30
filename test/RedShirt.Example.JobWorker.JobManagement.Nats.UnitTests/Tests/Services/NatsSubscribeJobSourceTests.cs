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
using System.Reflection;
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

    private static NatsSubscribeJobSource CreateJobSource(
        Mock<INatsConnectionRetryWrapper> connectionRetryWrapper,
        Mock<IJobSubscriberIntakeQueue>? intakeQueue = null,
        Mock<IExecutionEndArbiter>? executionEndArbiter = null,
        Mock<ISleepService>? sleepService = null,
        bool haltOnFailure = true,
        int fetchCount = 5,
        bool treatTransientExceptionAsFailure = false)
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

        if (executionEndArbiter is null)
        {
            executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
            executionEndArbiter
                .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
                .Returns(new TaskCompletionSource().Task);
        }

        return new NatsSubscribeJobSource(
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
            NullLogger<NatsSubscribeJobSource>.Instance);
    }

    private static Mock<INatsConnectionRetryWrapper> CreatePassthroughWrapper(INatsJSConsumer consumer)
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        wrapper.Setup(w => w.ResetConnection());
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

    private static void SetupBlockingConsume(Mock<INatsJSConsumer> consumer)
    {
        consumer
            .Setup(c => c.ConsumeAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.IsAny<NatsJSConsumeOpts>(),
                It.IsAny<CancellationToken>()))
            .Returns((INatsDeserialize<NatsMemoryOwner<byte>> _, NatsJSConsumeOpts __, CancellationToken ct) =>
                WaitUntilCancelledAsync(ct));
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

    private static Task InvokeWaitThenStopSubscriberAsync(NatsSubscribeJobSource jobSource,
        CancellationToken cancellationToken)
    {
        var method = typeof(NatsSubscribeJobSource).GetMethod("WaitThenStopSubscriberAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task) method.Invoke(jobSource, [cancellationToken])!;
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Empty)]
    public async Task AcknowledgeAsync_AlwaysAcks(CoreJobResult result)
    {
        var message = new Mock<INatsJSMsg<NatsMemoryOwner<byte>>>(MockBehavior.Strict);
        message
            .Setup(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken))
            .Returns(ValueTask.CompletedTask);

        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new NatsRawJobModel
        {
            Message = message.Object,
            MessageId = "m",
            CreatedAtUtc = DateTime.UtcNow
        }, result, TestContext.Current.CancellationToken);

        message.Verify(m => m.AckAsync(It.IsAny<AckOpts?>(), TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenIncompatibleMessage_DoesNotAck()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        wrapper.Verify(w => w.GetConsumerAndDoActionWithRetryAsync(
            It.IsAny<Func<INatsJSConsumer, CancellationToken, Task>>(),
            It.IsAny<bool>(),
            It.IsAny<Action<INatsConnection>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_ThrowsNotSupportedException()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HeartbeatAsync_Completes()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        await jobSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [Fact]
    public void IsSubscriptionSource_IsTrue()
    {
        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(wrapper);

        Assert.True(jobSource.IsSubscriptionSource);
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task StartSubscriberAsync_ConsumesStreamAndWaitsForFinished()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        SetupBlockingConsume(consumer);

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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var subscribeTask = jobSource.StartSubscriberAsync(cts.Token);

        await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(waitForFinishedExecuted);
        executionEndArbiter.Verify(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(
            c => c.ConsumeAsync(
                It.IsAny<INatsDeserialize<NatsMemoryOwner<byte>>>(),
                It.Is<NatsJSConsumeOpts>(o => o.MaxMsgs == 5),
                cts.Token),
            Times.Once);

        await cts.CancelAsync();
        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartSubscriberAsync_WhenMessageReceived_LoadsIntakeQueue()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        var message = CreateMessage("payload", 99);
        SetupConsumeMessages(consumer, message);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        IJobSourceResponse? loaded = null;
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded = response);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var job = Assert.IsType<NatsRawJobModel>(Assert.Single(loaded!.Items));
        Assert.Equal("99", job.MessageId);
        Assert.Equal("99", job.IdempotencyId);
        Assert.Equal("payload", job.Body);
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
        IJobSourceResponse? loaded = null;
        intakeQueue
            .Setup(q => q.Load(It.IsAny<IJobSourceResponse>()))
            .Callback<IJobSourceResponse>(response => loaded = response);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var jobSource = CreateJobSource(wrapper, intakeQueue);

        await jobSource.StartSubscriberAsync(TestContext.Current.CancellationToken);

        var job = Assert.IsType<NatsRawJobModel>(Assert.Single(loaded!.Items));
        Assert.Equal("UNKNOWN", job.MessageId);
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
    public async Task StartSubscriberAsync_WhenTransientFailure_RetriesUntilSuccess()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        SetupBlockingConsume(consumer);

        var wrapper = new Mock<INatsConnectionRetryWrapper>(MockBehavior.Strict);
        var attempts = 0;
        wrapper.Setup(w => w.ResetConnection());
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var subscribeTask = jobSource.StartSubscriberAsync(cts.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(2, attempts);

        await cts.CancelAsync();
        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitThenStopSubscriberAsync_WhenFinished_Completes()
    {
        var consumer = new Mock<INatsJSConsumer>(MockBehavior.Strict);
        SetupBlockingConsume(consumer);

        var wrapper = CreatePassthroughWrapper(consumer.Object);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.WaitForFinishedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobSource = CreateJobSource(wrapper, executionEndArbiter: executionEndArbiter);

        await InvokeWaitThenStopSubscriberAsync(jobSource, TestContext.Current.CancellationToken);
    }
}