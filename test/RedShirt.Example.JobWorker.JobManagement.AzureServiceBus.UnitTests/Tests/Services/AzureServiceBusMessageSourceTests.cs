using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services;

public class AzureServiceBusMessageSourceTests
{
    private const int MaxMessagesPerRequest = 10;

    [Theory]
    [InlineData(75, 50)]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public async Task Test_GetMessages(int numberOfMessagesInQueue, int batchSize)
    {
        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesInQueue);
        var expectedMessagesRemaining = numberOfMessagesInQueue - expectedNumberOfMessagesRetrieved;

        var queue = new Queue<string>();
        var queueCheckList = new List<IServiceBusMessageContainer>();
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(client.Object);

        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), TestContext.Current.CancellationToken))
            .ReturnsAsync((int callbackBatchSize, CancellationToken _) =>
            {
                var response = new List<IServiceBusMessageContainer>();

                Assert.True(callbackBatchSize > 0, $"Invalid message count: {callbackBatchSize}");
                Assert.True(callbackBatchSize <= MaxMessagesPerRequest,
                    $"Invalid message count: {callbackBatchSize}");

                for (var i = 0; i < callbackBatchSize; i++)
                {
                    if (!queue.TryDequeue(out var _))
                    {
                        break;
                    }

                    var msg = new Mock<IServiceBusMessageContainer>();
                    queueCheckList.Add(msg.Object);
                    response.Add(msg.Object);
                }

                return response;
            });

        var options = new AzureServiceBusConfigurationModel
        {
            MaxMessagesPerRequest = MaxMessagesPerRequest,
            VisibilityTimeoutSeconds = 0 // Not used in these tests
        };
        var messageSource = new AzureServiceBusMessageSource(source.Object, Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(messages);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);
        foreach (var msg in messages)
        {
            Assert.Contains(msg, queueCheckList);
        }

        var expectedNumberOfInvocations =
            Math.Max(1,
                expectedNumberOfMessagesRetrieved / MaxMessagesPerRequest +
                (expectedNumberOfMessagesRetrieved % MaxMessagesPerRequest > 0 ? 1 : 0));

        Assert.Equal(expectedNumberOfInvocations, client.Invocations.Count);
        Assert.Equal(expectedMessagesRemaining, queue.Count);

        client.Verify(
            a => a.GetMessagesAsync(
                It.IsAny<int>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedNumberOfInvocations));
    }
}