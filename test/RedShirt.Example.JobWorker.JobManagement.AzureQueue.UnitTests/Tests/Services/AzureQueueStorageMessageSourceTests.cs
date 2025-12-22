using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Services;

public class AzureQueueStorageMessageSourceTests
{
    private const int MaxMessagesPerRequest = 32;

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
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClient())
            .Returns(client.Object);

        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync((int callbackBatchSize, TimeSpan _, CancellationToken _) =>
            {
                var response = new List<IQueueMessageModel>();

                Assert.True(callbackBatchSize > 0, $"Invalid message count: {callbackBatchSize}");
                Assert.True(callbackBatchSize <= MaxMessagesPerRequest,
                    $"Invalid message count: {callbackBatchSize}");

                for (var i = 0; i < callbackBatchSize; i++)
                {
                    if (!queue.TryDequeue(out var messageBody))
                    {
                        break;
                    }

                    var msg = new Mock<IQueueMessageModel>();
                    msg.Setup(m => m.Body).Returns(messageBody);
                    response.Add(msg.Object);
                }

                return response;
            });

        var visibilityTimeout = new Random().Next(20, 59);
        var options = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = visibilityTimeout
        };

        var messageSource = new AzureQueueStorageMessageSource(source.Object, Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(messages);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        var expectedNumberOfInvocations =
            Math.Max(1,
                expectedNumberOfMessagesRetrieved / MaxMessagesPerRequest +
                (expectedNumberOfMessagesRetrieved % MaxMessagesPerRequest > 0 ? 1 : 0));

        Assert.Equal(expectedNumberOfInvocations, client.Invocations.Count);
        Assert.Equal(expectedMessagesRemaining, queue.Count);

        client.Verify(
            a => a.GetMessagesAsync(
                It.IsAny<int>(), It.Is<TimeSpan>(ts => ts.Seconds == options.EffectiveVisibilityTimeoutSeconds),
                TestContext.Current.CancellationToken),
            Times.Exactly(expectedNumberOfInvocations));
    }
}