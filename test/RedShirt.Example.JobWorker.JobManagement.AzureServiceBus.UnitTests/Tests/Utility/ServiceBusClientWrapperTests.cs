using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Utility;

public class ServiceBusClientWrapperTests
{
    private static IServiceBusMessageContainer CreateContainer(ServiceBusReceivedMessage message)
    {
        return new ServiceBusClientWrapper.ServiceBusMessageContainer
        {
            Message = message
        };
    }

    [Fact]
    public async Task AbandonMessageAsync_ForwardsToReceiver()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var container = CreateContainer(message);
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.AbandonMessageAsync(message, null, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        await wrapper.AbandonMessageAsync(container, TestContext.Current.CancellationToken);

        receiver.Verify(r => r.AbandonMessageAsync(message, null, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public void Client_ExposesInjectedReceiver()
    {
        var receiver = new Mock<ServiceBusReceiver>();
        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        Assert.Same(receiver.Object, wrapper.Client);
    }

    [Fact]
    public async Task CompleteMessageAsync_ForwardsToReceiver()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var container = CreateContainer(message);
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.CompleteMessageAsync(message, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        await wrapper.CompleteMessageAsync(container, TestContext.Current.CancellationToken);

        receiver.Verify(r => r.CompleteMessageAsync(message, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task DeadLetterMessageAsync_AllowsNullDescription()
    {
        const string reason = "processing-failed";
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var container = CreateContainer(message);
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.DeadLetterMessageAsync(message, reason, null, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        await wrapper.DeadLetterMessageAsync(container, reason,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(receiver.Invocations);
        receiver.Verify(
            r => r.DeadLetterMessageAsync(message, reason, null, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task DeadLetterMessageAsync_ForwardsToReceiver()
    {
        const string reason = "processing-failed";
        const string description = "could not deserialize";
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var container = CreateContainer(message);
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.DeadLetterMessageAsync(message, reason, description, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        await wrapper.DeadLetterMessageAsync(container, reason, description, TestContext.Current.CancellationToken);

        Assert.Single(receiver.Invocations);
        receiver.Verify(
            r => r.DeadLetterMessageAsync(message, reason, description, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_ReceivesMessagesAndWrapsThem()
    {
        const int maxMessages = 3;
        var message1 = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("one"));
        var message2 = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("two"));
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken))
            .ReturnsAsync([message1, message2]);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        var results =
            (await wrapper.GetMessagesAsync(maxMessages, null, TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Same(message1, results[0].Message);
        Assert.Same(message2, results[1].Message);
        Assert.All(results, r => Assert.IsType<ServiceBusClientWrapper.ServiceBusMessageContainer>(r));

        receiver.Verify(
            r => r.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken),
            Times.Once);
    }

    /// <summary>
    ///     Spun off from GetMessagesAsync_ReceivesMessagesAndWrapsThem, plus checking interpretation of wait times.
    /// </summary>
    [Theory]
    [InlineData(null, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    public async Task GetMessagesAsync_ReceivesMessagesAndWrapsThem_AndWaitTimes(int? requestedWaitTimeSeconds,
        int expectedWaitTimeSeconds)
    {
        const int maxMessages = 3;
        var message1 = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("one"));
        var message2 = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("two"));
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(expectedWaitTimeSeconds),
                TestContext.Current.CancellationToken))
            .ReturnsAsync([message1, message2]);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        var results =
            (await wrapper.GetMessagesAsync(maxMessages, requestedWaitTimeSeconds,
                TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Same(message1, results[0].Message);
        Assert.Same(message2, results[1].Message);
        Assert.All(results, r => Assert.IsType<ServiceBusClientWrapper.ServiceBusMessageContainer>(r));

        receiver.Verify(
            r => r.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(expectedWaitTimeSeconds),
                TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsEmptyWhenReceiverReturnsEmpty()
    {
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.ReceiveMessagesAsync(It.IsAny<int>(), TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken))
            .ReturnsAsync([]);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        var results = await wrapper.GetMessagesAsync(5, null, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        receiver.Verify(
            r => r.ReceiveMessagesAsync(5, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task RenewMessageLockAsync_ForwardsToReceiver()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var container = CreateContainer(message);
        var receiver = new Mock<ServiceBusReceiver>();
        receiver
            .Setup(r => r.RenewMessageLockAsync(message, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var wrapper = new ServiceBusClientWrapper(receiver.Object);

        await wrapper.RenewMessageLockAsync(container, TestContext.Current.CancellationToken);

        Assert.Single(receiver.Invocations);
        receiver.Verify(r => r.RenewMessageLockAsync(message, TestContext.Current.CancellationToken), Times.Once);
    }
}