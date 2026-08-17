using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Services;

public class SqsMessageSourceTests
{
    [Theory]
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

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceiveMessageRequest req, CancellationToken _) =>
            {
                var response = new ReceiveMessageResponse
                {
                    Messages = []
                };

                Assert.True(req.MaxNumberOfMessages > 0, $"Invalid message count: {req.MaxNumberOfMessages}");
                Assert.True(req.MaxNumberOfMessages <= 10, $"Invalid message count: {req.MaxNumberOfMessages}");

                for (var i = 0; i < req.MaxNumberOfMessages; i++)
                {
                    if (!queue.TryDequeue(out var messageBody))
                    {
                        break;
                    }

                    response.Messages.Add(new Message {Body = messageBody});
                }

                return response;
            });

        var queueUrl = Guid.NewGuid().ToString();
        var visibilityTimeout = Random.Shared.Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout,
            DlqNotEnabled = true,
            MaximumReceives = 1,
            WaitTimeSeconds = 0
        };

        var messageSource = new SqsMessageSource(sqs.Object, new PassthroughRetryWrapper(), Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(messages);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        var expectedNumberOfInvocations =
            Math.Max(1, expectedNumberOfMessagesRetrieved / 10 + (expectedNumberOfMessagesRetrieved % 10 > 0 ? 1 : 0));

        Assert.Equal(expectedNumberOfInvocations, sqs.Invocations.Count);
        Assert.Equal(expectedMessagesRemaining, queue.Count);

        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout
                    && r.WaitTimeSeconds == null),
                TestContext.Current.CancellationToken), Times.Exactly(expectedNumberOfInvocations));
    }

    /// <summary>
    ///     Confirm that the message source won't explode if the ReceiveMessageResponse from
    ///     SQS is null. A version of the AWS SDK's SQS package has been known to do this when there
    ///     are no messages to receive.
    /// </summary>
    [Fact]
    public async Task Test_GetMessages_NullResponse()
    {
        var batchSize = 1;

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceiveMessageResponse) null!);

        var queueUrl = Guid.NewGuid().ToString();
        var visibilityTimeout = Random.Shared.Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout,
            DlqNotEnabled = true,
            MaximumReceives = 1,
            WaitTimeSeconds = 0
        };

        var messageSource = new SqsMessageSource(sqs.Object, new PassthroughRetryWrapper(), Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Empty(messages);

        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout),
                TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Spun off of a copy of Test_GetMessages, ensure that the configured WaitTimeSeconds is being enforced only for the
    ///     first fetch request.
    /// </summary>
    /// <param name="numberOfMessagesInQueue"></param>
    /// <param name="batchSize"></param>
    [Theory]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public async Task Test_GetMessages_WaitTimeSeconds(int numberOfMessagesInQueue, int batchSize)
    {
        const int waitTimeSeconds = 10;

        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesInQueue);
        var expectedMessagesRemaining = numberOfMessagesInQueue - expectedNumberOfMessagesRetrieved;

        var queue = new Queue<string>();
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceiveMessageRequest req, CancellationToken _) =>
            {
                var response = new ReceiveMessageResponse
                {
                    Messages = []
                };

                Assert.True(req.MaxNumberOfMessages > 0, $"Invalid message count: {req.MaxNumberOfMessages}");
                Assert.True(req.MaxNumberOfMessages <= 10, $"Invalid message count: {req.MaxNumberOfMessages}");

                for (var i = 0; i < req.MaxNumberOfMessages; i++)
                {
                    if (!queue.TryDequeue(out var messageBody))
                    {
                        break;
                    }

                    response.Messages.Add(new Message {Body = messageBody});
                }

                return response;
            });

        var queueUrl = Guid.NewGuid().ToString();
        var visibilityTimeout = Random.Shared.Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout,
            DlqNotEnabled = true,
            MaximumReceives = 1,
            WaitTimeSeconds = waitTimeSeconds
        };

        var messageSource = new SqsMessageSource(sqs.Object, new PassthroughRetryWrapper(), Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(messages);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        var expectedNumberOfInvocations =
            Math.Max(1, expectedNumberOfMessagesRetrieved / 10 + (expectedNumberOfMessagesRetrieved % 10 > 0 ? 1 : 0));

        Assert.Equal(expectedNumberOfInvocations, sqs.Invocations.Count);
        Assert.Equal(expectedMessagesRemaining, queue.Count);

        // Verify general
        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout),
                TestContext.Current.CancellationToken), Times.Exactly(expectedNumberOfInvocations));
        // Verify single call using wait time
        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout
                    && r.WaitTimeSeconds == waitTimeSeconds),
                TestContext.Current.CancellationToken), Times.Once);
        // Follow-up calls should not have a wait time in order to avoid stacking waits
        var expectedFollowUpCalls = expectedNumberOfInvocations - 1;
        if (expectedFollowUpCalls > 0)
        {
            sqs.Verify(
                a => a.ReceiveMessageAsync(
                    It.Is<ReceiveMessageRequest>(r =>
                        r.QueueUrl == queueUrl
                        && r.VisibilityTimeout == visibilityTimeout
                        && r.WaitTimeSeconds == null),
                    TestContext.Current.CancellationToken), Times.Exactly(expectedFollowUpCalls));
        }
    }

    /// <summary>
    ///     Spun off of a copy of WaitTimeSeconds, ensure that the interpretation of WaitTimeSeconds caps out at the SQS-set
    ///     maximum of 20 seconds.
    /// </summary>
    /// <param name="numberOfMessagesInQueue"></param>
    /// <param name="batchSize"></param>
    [Theory]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public async Task Test_GetMessages_WaitTimeSeconds_Maximum20(int numberOfMessagesInQueue, int batchSize)
    {
        const int waitTimeSeconds = 200; // Set well above 20

        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesInQueue);
        var expectedMessagesRemaining = numberOfMessagesInQueue - expectedNumberOfMessagesRetrieved;

        var queue = new Queue<string>();
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceiveMessageRequest req, CancellationToken _) =>
            {
                var response = new ReceiveMessageResponse
                {
                    Messages = []
                };

                Assert.True(req.MaxNumberOfMessages > 0, $"Invalid message count: {req.MaxNumberOfMessages}");
                Assert.True(req.MaxNumberOfMessages <= 10, $"Invalid message count: {req.MaxNumberOfMessages}");

                for (var i = 0; i < req.MaxNumberOfMessages; i++)
                {
                    if (!queue.TryDequeue(out var messageBody))
                    {
                        break;
                    }

                    response.Messages.Add(new Message {Body = messageBody});
                }

                return response;
            });

        var queueUrl = Guid.NewGuid().ToString();
        var visibilityTimeout = Random.Shared.Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout,
            DlqNotEnabled = true,
            MaximumReceives = 1,
            WaitTimeSeconds = waitTimeSeconds
        };

        var messageSource = new SqsMessageSource(sqs.Object, new PassthroughRetryWrapper(), Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(messages);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        var expectedNumberOfInvocations =
            Math.Max(1, expectedNumberOfMessagesRetrieved / 10 + (expectedNumberOfMessagesRetrieved % 10 > 0 ? 1 : 0));

        Assert.Equal(expectedNumberOfInvocations, sqs.Invocations.Count);
        Assert.Equal(expectedMessagesRemaining, queue.Count);

        // Verify general
        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout),
                TestContext.Current.CancellationToken), Times.Exactly(expectedNumberOfInvocations));
        // Verify single call using wait time
        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout
                    && r.WaitTimeSeconds == 20),
                TestContext.Current.CancellationToken), Times.Once);
        // Follow-up calls should not have a wait time in order to avoid stacking waits
        var expectedFollowUpCalls = expectedNumberOfInvocations - 1;
        if (expectedFollowUpCalls > 0)
        {
            sqs.Verify(
                a => a.ReceiveMessageAsync(
                    It.Is<ReceiveMessageRequest>(r =>
                        r.QueueUrl == queueUrl
                        && r.VisibilityTimeout == visibilityTimeout
                        && r.WaitTimeSeconds == null),
                    TestContext.Current.CancellationToken), Times.Exactly(expectedFollowUpCalls));
        }
    }

    /// <summary>
    ///     Spun off of a copy of WaitTimeSeconds, ensure that the interpretation of WaitTimeSeconds is not counted if the
    ///     value is negative.
    /// </summary>
    /// <param name="numberOfMessagesInQueue"></param>
    /// <param name="batchSize"></param>
    [Theory]
    [InlineData(25, 50)]
    [InlineData(50, 25)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public async Task Test_GetMessages_WaitTimeSeconds_Negative(int numberOfMessagesInQueue, int batchSize)
    {
        const int waitTimeSeconds = -1;

        var expectedNumberOfMessagesRetrieved = Math.Min(batchSize, numberOfMessagesInQueue);
        var expectedMessagesRemaining = numberOfMessagesInQueue - expectedNumberOfMessagesRetrieved;

        var queue = new Queue<string>();
        for (var i = 0; i < numberOfMessagesInQueue; i++)
        {
            queue.Enqueue($"Message {i}");
        }

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReceiveMessageRequest req, CancellationToken _) =>
            {
                var response = new ReceiveMessageResponse
                {
                    Messages = []
                };

                Assert.True(req.MaxNumberOfMessages > 0, $"Invalid message count: {req.MaxNumberOfMessages}");
                Assert.True(req.MaxNumberOfMessages <= 10, $"Invalid message count: {req.MaxNumberOfMessages}");

                for (var i = 0; i < req.MaxNumberOfMessages; i++)
                {
                    if (!queue.TryDequeue(out var messageBody))
                    {
                        break;
                    }

                    response.Messages.Add(new Message {Body = messageBody});
                }

                return response;
            });

        var queueUrl = Guid.NewGuid().ToString();
        var visibilityTimeout = Random.Shared.Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout,
            DlqNotEnabled = true,
            MaximumReceives = 1,
            WaitTimeSeconds = waitTimeSeconds
        };

        var messageSource = new SqsMessageSource(sqs.Object, new PassthroughRetryWrapper(), Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.NotNull(messages);

        Assert.Equal(expectedNumberOfMessagesRetrieved, messages.Count);

        var expectedNumberOfInvocations =
            Math.Max(1, expectedNumberOfMessagesRetrieved / 10 + (expectedNumberOfMessagesRetrieved % 10 > 0 ? 1 : 0));

        Assert.Equal(expectedNumberOfInvocations, sqs.Invocations.Count);
        Assert.Equal(expectedMessagesRemaining, queue.Count);

        // Verify general, all should be null
        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout
                    && r.WaitTimeSeconds == null),
                TestContext.Current.CancellationToken), Times.Exactly(expectedNumberOfInvocations));
    }

    private sealed class PassthroughRetryWrapper : ISqsJobSourceRetryWrapperService
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }

        public Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }
    }
}