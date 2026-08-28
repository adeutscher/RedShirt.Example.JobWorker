using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;
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
        var wrapper = AzureServiceBusRetryTestHelpers.CreatePassthroughClientRetryWrapper(client.Object);

        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int?>(), TestContext.Current.CancellationToken))
            .ReturnsAsync((int callbackBatchSize, int? _, CancellationToken _) =>
            {
                var response = new List<IServiceBusMessageContainer>();

                Assert.True(callbackBatchSize > 0, $"Invalid message count: {callbackBatchSize}");
                Assert.True(callbackBatchSize <= MaxMessagesPerRequest,
                    $"Invalid message count: {callbackBatchSize}");

                for (var i = 0; i < callbackBatchSize; i++)
                {
                    if (!queue.TryDequeue(out _))
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
            VisibilityTimeoutSeconds = 0, // Not used in these tests
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        };
        var messageSource = new AzureServiceBusMessageSource(wrapper.Object, Options.Create(options));

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
                It.IsAny<int>(), It.IsAny<int?>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedNumberOfInvocations));
    }

    /// <summary>
    ///     Spun off of a copy of Test_GetMessages, ensure that the configured WaitTimeSeconds is being enforced only for the
    ///     first fetch request.
    /// </summary>
    /// <param name="numberOfMessagesInQueue"></param>
    /// <param name="batchSize"></param>
    [Theory]
    [InlineData(75, 50)]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public async Task Test_GetMessages_WaitTimeSeconds(int numberOfMessagesInQueue, int batchSize)
    {
        const int waitTimeSeconds = 5;

        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesInQueue);
        var expectedMessagesRemaining = numberOfMessagesInQueue - expectedNumberOfMessagesRetrieved;

        var queue = new Queue<string>();
        var queueCheckList = new List<IServiceBusMessageContainer>();
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var client = new Mock<IServiceBusClientWrapper>();
        var wrapper = AzureServiceBusRetryTestHelpers.CreatePassthroughClientRetryWrapper(client.Object);

        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int?>(), TestContext.Current.CancellationToken))
            .ReturnsAsync((int callbackBatchSize, int? _, CancellationToken _) =>
            {
                var response = new List<IServiceBusMessageContainer>();

                Assert.True(callbackBatchSize > 0, $"Invalid message count: {callbackBatchSize}");
                Assert.True(callbackBatchSize <= MaxMessagesPerRequest,
                    $"Invalid message count: {callbackBatchSize}");

                for (var i = 0; i < callbackBatchSize; i++)
                {
                    if (!queue.TryDequeue(out _))
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
            VisibilityTimeoutSeconds = 0, // Not used in these tests
            WaitTimeSeconds = waitTimeSeconds,
            AbandonRecoveredFailuresOnAcknowledge = true
        };
        var messageSource = new AzureServiceBusMessageSource(wrapper.Object, Options.Create(options));

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
                It.IsAny<int>(), It.IsAny<int?>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedNumberOfInvocations));
        client.Verify(
            a => a.GetMessagesAsync(
                It.IsAny<int>(), waitTimeSeconds, TestContext.Current.CancellationToken),
            Times.Once);
        var expectedFollowUpCalls = expectedNumberOfInvocations - 1;
        if (expectedFollowUpCalls > 0)
        {
            client.Verify(
                a => a.GetMessagesAsync(
                    It.IsAny<int>(), null, TestContext.Current.CancellationToken),
                Times.Exactly(expectedFollowUpCalls));
        }
    }

    /// <summary>
    ///     Spun off of a copy of Test_GetMessages_WaitTimeSeconds, ensure that the configured WaitTimeSeconds floored at 0.
    /// </summary>
    /// <param name="numberOfMessagesInQueue"></param>
    /// <param name="batchSize"></param>
    [Theory]
    [InlineData(75, 50)]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public async Task Test_GetMessages_WaitTimeSeconds_Floored(int numberOfMessagesInQueue, int batchSize)
    {
        const int waitTimeSeconds = -1;

        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesInQueue);
        var expectedMessagesRemaining = numberOfMessagesInQueue - expectedNumberOfMessagesRetrieved;

        var queue = new Queue<string>();
        var queueCheckList = new List<IServiceBusMessageContainer>();
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var client = new Mock<IServiceBusClientWrapper>();
        var wrapper = AzureServiceBusRetryTestHelpers.CreatePassthroughClientRetryWrapper(client.Object);

        client
            .Setup(c => c.GetMessagesAsync(It.IsAny<int>(), It.IsAny<int?>(), TestContext.Current.CancellationToken))
            .ReturnsAsync((int callbackBatchSize, int? _, CancellationToken _) =>
            {
                var response = new List<IServiceBusMessageContainer>();

                Assert.True(callbackBatchSize > 0, $"Invalid message count: {callbackBatchSize}");
                Assert.True(callbackBatchSize <= MaxMessagesPerRequest,
                    $"Invalid message count: {callbackBatchSize}");

                for (var i = 0; i < callbackBatchSize; i++)
                {
                    if (!queue.TryDequeue(out _))
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
            VisibilityTimeoutSeconds = 0, // Not used in these tests
            WaitTimeSeconds = waitTimeSeconds,
            AbandonRecoveredFailuresOnAcknowledge = true
        };
        var messageSource = new AzureServiceBusMessageSource(wrapper.Object, Options.Create(options));

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
                It.IsAny<int>(), It.IsAny<int?>(), TestContext.Current.CancellationToken),
            Times.Exactly(expectedNumberOfInvocations));
        client.Verify(
            a => a.GetMessagesAsync(
                It.IsAny<int>(), 0, TestContext.Current.CancellationToken),
            Times.Once);
        var expectedFollowUpCalls = expectedNumberOfInvocations - 1;
        if (expectedFollowUpCalls > 0)
        {
            client.Verify(
                a => a.GetMessagesAsync(
                    It.IsAny<int>(), null, TestContext.Current.CancellationToken),
                Times.Exactly(expectedFollowUpCalls));
        }
    }
}