using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Utility;

public class QueueClientWrapperTests
{
    private static IQueueMessageModel CreateMessage(string messageId, string popReceipt)
    {
        var message = new Mock<IQueueMessageModel>();
        message.SetupGet(m => m.MessageId).Returns(messageId);
        message.SetupGet(m => m.PopReceipt).Returns(popReceipt);
        return message.Object;
    }

    [Fact]
    public async Task DeleteMessageAsync_ForwardsToClient()
    {
        var message = CreateMessage("msg-id", "pop-receipt");
        var client = new Mock<QueueClient>();
        client
            .Setup(c => c.DeleteMessageAsync("msg-id", "pop-receipt", TestContext.Current.CancellationToken))
            .ReturnsAsync(Mock.Of<Response>());

        var wrapper = new QueueClientWrapper(client.Object);

        await wrapper.DeleteMessageAsync(message, TestContext.Current.CancellationToken);

        client.Verify(c => c.DeleteMessageAsync("msg-id", "pop-receipt", TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_ReceivesMessagesAndWrapsThem()
    {
        const int maxMessages = 3;
        var visibilityTimeout = TimeSpan.FromSeconds(30);
        var message1 = QueuesModelFactory.QueueMessage("id-1", "pop-1", BinaryData.FromString("one"), 1);
        var message2 = QueuesModelFactory.QueueMessage("id-2", "pop-2", BinaryData.FromString("two"), 1);
        var client = new Mock<QueueClient>();
        client
            .Setup(c => c.ReceiveMessagesAsync(maxMessages, visibilityTimeout, TestContext.Current.CancellationToken))
            .ReturnsAsync(Response.FromValue(new[] {message1, message2}, Mock.Of<Response>()));

        var wrapper = new QueueClientWrapper(client.Object);

        var results = await wrapper.GetMessagesAsync(maxMessages, visibilityTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.IsType<QueueClientWrapper.QueueMessageModel>(r));
        Assert.Equal("id-1", results[0].MessageId);
        Assert.Equal("pop-1", results[0].PopReceipt);
        Assert.Equal("one", results[0].Body);
        Assert.Equal("id-2", results[1].MessageId);
        Assert.Equal("pop-2", results[1].PopReceipt);
        Assert.Equal("two", results[1].Body);

        client.Verify(
            c => c.ReceiveMessagesAsync(maxMessages, visibilityTimeout, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsEmptyWhenResponseIsNull()
    {
        var visibilityTimeout = TimeSpan.FromSeconds(15);
        var client = new Mock<QueueClient>();
        client
            .Setup(c => c.ReceiveMessagesAsync(It.IsAny<int?>(), It.IsAny<TimeSpan?>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync((Response<QueueMessage[]>) null!);

        var wrapper = new QueueClientWrapper(client.Object);

        var results = await wrapper.GetMessagesAsync(5, visibilityTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        client.Verify(
            c => c.ReceiveMessagesAsync(5, visibilityTimeout, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsEmptyWhenResponseValueIsEmpty()
    {
        var visibilityTimeout = TimeSpan.FromSeconds(15);
        var client = new Mock<QueueClient>();
        client
            .Setup(c => c.ReceiveMessagesAsync(It.IsAny<int?>(), It.IsAny<TimeSpan?>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(Response.FromValue(Array.Empty<QueueMessage>(), Mock.Of<Response>()));

        var wrapper = new QueueClientWrapper(client.Object);

        var results = await wrapper.GetMessagesAsync(5, visibilityTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        client.Verify(
            c => c.ReceiveMessagesAsync(5, visibilityTimeout, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public void QueueClient_ExposesInjectedClient()
    {
        var client = new Mock<QueueClient>();
        var wrapper = new QueueClientWrapper(client.Object);

        Assert.Same(client.Object, wrapper.QueueClient);
    }

    [Fact]
    public async Task SetMessageVisibilityTimeoutAsync_ForwardsToClient()
    {
        var visibilityTimeout = TimeSpan.FromSeconds(45);
        var message = CreateMessage("msg-id", "pop-receipt");
        var client = new Mock<QueueClient>();
        client
            .Setup(c => c.UpdateMessageAsync("msg-id", "pop-receipt", (string?) null, visibilityTimeout,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(Response.FromValue(
                QueuesModelFactory.UpdateReceipt("new-pop", DateTimeOffset.UtcNow),
                Mock.Of<Response>()));

        var wrapper = new QueueClientWrapper(client.Object);

        await wrapper.SetMessageVisibilityTimeoutAsync(message, visibilityTimeout,
            TestContext.Current.CancellationToken);

        client.Verify(
            c => c.UpdateMessageAsync("msg-id", "pop-receipt", (string?) null, visibilityTimeout,
                TestContext.Current.CancellationToken),
            Times.Once);
    }
}