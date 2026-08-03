using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Enums;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;
using System.Net;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Services;

public class SqsPoisonMessagesHandlerTests
{
    private static SqsPoisonMessagesHandler CreateHandler(Mock<IAmazonSQS> sqs, SqsConfigurationModel config)
    {
        return new SqsPoisonMessagesHandler(sqs.Object, new PassthroughRetryWrapper(), Options.Create(config));
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

    [Fact]
    public async Task WhenDlqEnabled_ReturnsEnforcementNotEnabled()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var queueUrl = Guid.NewGuid().ToString();
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = queueUrl,
            VisibilityTimeoutSeconds = 30,
            DlqNotEnabled = false,
            MaximumReceives = 1
        });

        var message = CreateMessage(100);

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.EnforcementNotEnabled, outcome);
        Assert.Empty(sqs.Invocations);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(1, 1)]
    [InlineData(1, 0)] // EffectiveMaximumReceives floors at 1
    public async Task WhenReceiveCountAtOrAboveMaximum_ReturnsEnforced(int receiveCount, int maximumReceives)
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
            DlqNotEnabled = true,
            MaximumReceives = maximumReceives
        });

        var receiptHandle = Guid.NewGuid().ToString();
        var message = CreateMessage(receiveCount, receiptHandle);

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.Enforced, outcome);
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
    public async Task WhenReceiveCountAttributeMissing_ReturnsNotEnforced()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = 30,
            DlqNotEnabled = true,
            MaximumReceives = 1
        });

        var message = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Attributes = new Dictionary<string, string>()
        };

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.NotEnforced, outcome);
        Assert.Empty(sqs.Invocations);
    }

    [Fact]
    public async Task WhenReceiveCountAttributeUnparseable_ReturnsNotEnforced()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = 30,
            DlqNotEnabled = true,
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

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.NotEnforced, outcome);
        Assert.Empty(sqs.Invocations);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    [InlineData(0, 1)]
    public async Task WhenReceiveCountBelowMaximum_ReturnsNotEnforced(int receiveCount, int maximumReceives)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var handler = CreateHandler(sqs, new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = 30,
            DlqNotEnabled = true,
            MaximumReceives = maximumReceives
        });

        var message = CreateMessage(receiveCount);

        var outcome = await handler.AttemptPoisonMessageEnforcementAsync(message,
            TestContext.Current.CancellationToken);

        Assert.Equal(PoisonEnforcementResult.NotEnforced, outcome);
        Assert.Empty(sqs.Invocations);
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