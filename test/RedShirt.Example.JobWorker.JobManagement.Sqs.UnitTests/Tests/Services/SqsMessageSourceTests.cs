using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.UnitTests.Tests.Services;

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
        var visibilityTimeout = new Random().Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout
        };

        var messageSource = new SqsMessageSource(sqs.Object, Options.Create(options));

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
                    && r.VisibilityTimeout == visibilityTimeout),
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
        var visibilityTimeout = new Random().Next(100, 200);
        var options = new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = visibilityTimeout
        };

        var messageSource = new SqsMessageSource(sqs.Object, Options.Create(options));

        var messages = await messageSource.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Empty(messages);

        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.VisibilityTimeout == visibilityTimeout),
                TestContext.Current.CancellationToken), Times.Once);
    }
}