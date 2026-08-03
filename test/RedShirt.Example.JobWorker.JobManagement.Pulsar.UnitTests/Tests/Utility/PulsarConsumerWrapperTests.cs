using Microsoft.FSharp.Core;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
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
    public async Task AcknowledgeAsync_AcknowledgesMessage()
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
    public async Task AcknowledgeAsync_RoutesThroughRetryWrapper()
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
        await wrapper.AcknowledgeAsync(message.Object, TestContext.Current.CancellationToken);

        Assert.Equal(1, retryCalls);
        consumer.Verify(c => c.AcknowledgeAsync(It.IsAny<MessageId>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenMessageIdNull_DoesNotCallConsumerOrRetry()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        var retry = new Mock<IPulsarRetryWrapperService>(MockBehavior.Strict);
        var message = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message.SetupGet(m => m.PulsarMessageId).Returns((MessageId?) null);

        var wrapper = CreateWrapper(consumer.Object, retry.Object);
        await wrapper.AcknowledgeAsync(message.Object, TestContext.Current.CancellationToken);

        consumer.VerifyNoOtherCalls();
        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NegativeAcknowledgeAsync_NegativelyAcknowledgesMessage()
    {
        var consumer = new Mock<IConsumer<string>>(MockBehavior.Strict);
        consumer
            .Setup(c => c.NegativeAcknowledge(It.IsAny<MessageId>()))
            .Returns(CompletedUnitTask());

        var id = CreateMessageId(1, 10, 0);
        var message = new Mock<IPulsarMessageContainer>(MockBehavior.Strict);
        message.SetupGet(m => m.PulsarMessageId).Returns(id);

        var wrapper = CreateWrapper(consumer.Object);
        await wrapper.NegativeAcknowledgeAsync(message.Object, TestContext.Current.CancellationToken);

        consumer.Verify(c => c.NegativeAcknowledge(id), Times.Once);
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
}
