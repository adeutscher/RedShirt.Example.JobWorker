using Microsoft.FSharp.Core;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Utility;

public class PulsarConsumerWrapperTests
{
    private static Task<Unit> CompletedUnitTask()
    {
        return Task.FromResult(default(Unit)!);
    }

    private static MessageId CreateMessageId(long ledgerId, long entryId, int partition)
    {
        return new MessageId(ledgerId, entryId, MessageIdType.Single, partition, "orders", null);
    }

    private static PulsarConsumerWrapper CreateWrapper(
        IConsumer<string> consumer,
        IPulsarRetryWrapperService? retryWrapper = null)
    {
        return new PulsarConsumerWrapper(
            retryWrapper ?? PulsarRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            null,
            consumer,
            "orders");
    }

    [Fact]
    public async Task AcknowledgeAsync_SingleMessage_DelegatesToCommit()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.AcknowledgeAsync(It.IsAny<MessageId>()))
            .Returns(CompletedUnitTask());

        var id = CreateMessageId(1, 5, 0);
        var message = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message.SetupGet(m => m.PulsarMessageId).Returns(id);

        var wrapper = CreateWrapper(consumer.Object);
        await wrapper.AcknowledgeAsync(message.Object, TestContext.Current.CancellationToken);

        consumer.Verify(c => c.AcknowledgeAsync(id), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_AcknowledgesEachMessageIndividually()
    {
        var acknowledged = new List<MessageId>();
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.AcknowledgeAsync(It.IsAny<MessageId>()))
            .Returns<MessageId>(id =>
            {
                acknowledged.Add(id);
                return CompletedUnitTask();
            });

        var id1 = CreateMessageId(1, 10, 0);
        var id2 = CreateMessageId(1, 20, 1);

        var message1 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message1.SetupGet(m => m.PulsarMessageId).Returns(id1);
        var message2 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message2.SetupGet(m => m.PulsarMessageId).Returns(id2);

        var wrapper = CreateWrapper(consumer.Object);
        await wrapper.CommitAsync([message1.Object, message2.Object], TestContext.Current.CancellationToken);

        Assert.Equal([id1, id2], acknowledged);
        consumer.Verify(c => c.AcknowledgeAsync(It.IsAny<MessageId>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CommitAsync_RoutesEachAckThroughRetryWrapper()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.AcknowledgeAsync(It.IsAny<MessageId>()))
            .Returns(CompletedUnitTask());

        var retryCalls = 0;
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) =>
            {
                retryCalls++;
                return func(token);
            });

        var message = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message.SetupGet(m => m.PulsarMessageId).Returns(CreateMessageId(1, 5, 0));

        var wrapper = CreateWrapper(consumer.Object, retry.Object);
        await wrapper.CommitAsync([message.Object], TestContext.Current.CancellationToken);

        Assert.Equal(1, retryCalls);
        consumer.Verify(c => c.AcknowledgeAsync(It.IsAny<MessageId>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WhenNoMessages_DoesNotCallConsumerOrRetry()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        var wrapper = CreateWrapper(consumer.Object, retry.Object);

        await wrapper.CommitAsync([], TestContext.Current.CancellationToken);

        consumer.VerifyNoOtherCalls();
        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommitAsync_WhenPermanentNonCriticalFailure_ContinuesAcknowledgingOthers()
    {
        var acknowledged = new List<MessageId>();
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.AcknowledgeAsync(It.IsAny<MessageId>()))
            .Returns<MessageId>(id =>
            {
                acknowledged.Add(id);
                return CompletedUnitTask();
            });

        var permanent = new WorkerJobSourceException("ack failed", false, false, true);
        var retryAttempts = 0;
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, token) =>
            {
                retryAttempts++;
                if (retryAttempts == 1)
                {
                    return Task.FromException(permanent);
                }

                return func(token);
            });

        var id1 = CreateMessageId(1, 1, 0);
        var id2 = CreateMessageId(1, 2, 0);
        var id3 = CreateMessageId(1, 3, 1);

        var message1 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message1.SetupGet(m => m.PulsarMessageId).Returns(id1);
        var message2 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message2.SetupGet(m => m.PulsarMessageId).Returns(id2);
        var message3 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message3.SetupGet(m => m.PulsarMessageId).Returns(id3);

        var wrapper = CreateWrapper(consumer.Object, retry.Object);
        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() => wrapper.CommitAsync(
            [message1.Object, message2.Object, message3.Object],
            TestContext.Current.CancellationToken));

        Assert.Same(permanent, thrown);
        Assert.Equal([id2, id3], acknowledged);
        Assert.Equal(3, retryAttempts);
    }

    [Fact]
    public async Task ConsumeAsync_WhenTimedOut_ReturnsNull()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.ReceiveAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            });

        var wrapper = CreateWrapper(consumer.Object);
        var message = await wrapper.ConsumeAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        Assert.Null(message);
    }

    [Fact]
    public async Task DisposeAsync_DisposesUnderlyingConsumer()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var wrapper = CreateWrapper(consumer.Object);
        await wrapper.DisposeAsync();
        await wrapper.DisposeAsync();

        consumer.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task NegativeAcknowledgeAsync_NegativelyAcknowledgesEachMessageIndividually()
    {
        var nacked = new List<MessageId>();
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.NegativeAcknowledge(It.IsAny<MessageId>()))
            .Returns<MessageId>(id =>
            {
                nacked.Add(id);
                return CompletedUnitTask();
            });

        var id1 = CreateMessageId(1, 10, 0);
        var id2 = CreateMessageId(1, 20, 1);

        var message1 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message1.SetupGet(m => m.PulsarMessageId).Returns(id1);
        var message2 = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message2.SetupGet(m => m.PulsarMessageId).Returns(id2);

        var wrapper = CreateWrapper(consumer.Object);
        await wrapper.NegativeAcknowledgeAsync([message1.Object, message2.Object],
            TestContext.Current.CancellationToken);

        Assert.Equal([id1, id2], nacked);
        consumer.Verify(c => c.NegativeAcknowledge(It.IsAny<MessageId>()), Times.Exactly(2));
    }
}