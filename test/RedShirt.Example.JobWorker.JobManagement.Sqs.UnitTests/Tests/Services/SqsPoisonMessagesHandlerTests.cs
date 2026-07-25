using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;
using System.Net;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Services;

public class SqsPoisonMessagesHandlerTests
{
    [Fact]
    public async Task WhenDlqEnabled_DoesNotDelete()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var queueUrl = Guid.NewGuid().ToString();
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = 30,
            DlqEnabled = true,
            MaximumReceives = 1
        });

        var message = CreateMessage(receiveCount: 100);

        await handler.AttemptPoisonMessageEnforcementAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    [InlineData(0, 1)]
    public async Task WhenReceiveCountBelowMaximum_DoesNotDelete(int receiveCount, int maximumReceives)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = 30,
            DlqEnabled = false,
            MaximumReceives = maximumReceives
        });

        var message = CreateMessage(receiveCount: receiveCount);

        await handler.AttemptPoisonMessageEnforcementAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(1, 1)]
    [InlineData(1, 0)] // EffectiveMaximumReceives floors at 1
    public async Task WhenReceiveCountAtOrAboveMaximum_DeletesMessage(int receiveCount, int maximumReceives)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            });

        var queueUrl = Guid.NewGuid().ToString();
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = 30,
            DlqEnabled = false,
            MaximumReceives = maximumReceives
        });

        var receiptHandle = Guid.NewGuid().ToString();
        var message = CreateMessage(receiveCount: receiveCount, receiptHandle: receiptHandle);

        await handler.AttemptPoisonMessageEnforcementAsync(message, TestContext.Current.CancellationToken);

        sqs.Verify(
            s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sqs.Verify(
            s => s.DeleteMessageAsync(
                It.Is<DeleteMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.ReceiptHandle == receiptHandle),
                TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task WhenReceiveCountAttributeMissing_DoesNotDelete()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = 30,
            DlqEnabled = false,
            MaximumReceives = 1
        });

        var message = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Attributes = new Dictionary<string, string>()
        };

        await handler.AttemptPoisonMessageEnforcementAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    [Fact]
    public async Task WhenReceiveCountAttributeUnparseable_DoesNotDelete()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = 30,
            DlqEnabled = false,
            MaximumReceives = 1
        });

        var message = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateReceiveCount] = "not-a-number"
            }
        };

        await handler.AttemptPoisonMessageEnforcementAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    private static SqsPoisonMessagesHandler CreateHandler(Mock<IAmazonSQS> sqs, SqsConfigurationModel config)
    {
        return new SqsPoisonMessagesHandler(sqs.Object, Options.Create(config));
    }

    private static Message CreateMessage(int receiveCount, string? receiptHandle = null)
    {
        return new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = receiptHandle ?? Guid.NewGuid().ToString(),
            Attributes = new Dictionary<string, string>
            {
                [SqsConstants.AttributeApproximateReceiveCount] = receiveCount.ToString()
            }
        };
    }
}
