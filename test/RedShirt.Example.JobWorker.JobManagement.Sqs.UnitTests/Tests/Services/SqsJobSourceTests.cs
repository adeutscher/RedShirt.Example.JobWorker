using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Enums;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Models;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;
using System.Net;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Services;

public class SqsJobSourceTests
{
    private static SqsConfigurationModel CreateConfig(string? queueUrl = null, int visibilityTimeoutSeconds = 0,
        bool dlqNotEnabled = false, int maximumReceives = 1)
    {
        return new SqsConfigurationModel
        {
            QueueUrl = queueUrl ?? Guid.NewGuid().ToString(),
            VisibilityTimeoutSeconds = visibilityTimeoutSeconds,
            DlqNotEnabled = dlqNotEnabled,
            MaximumReceives = maximumReceives
        };
    }

    [Fact]
    public async Task TestGetJobsAsync()
    {
        const int batchSize = 10;

        var messageId1 = Guid.NewGuid().ToString();
        var receiptHandle1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var messageId2 = Guid.NewGuid().ToString();
        var receiptHandle2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data3Message = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = data3
        };
        var data4 = Guid.NewGuid().ToString();
        var data4Message = new Message
        {
            MessageId = Guid.NewGuid().ToString(),
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = data4
        };

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var sqsMessageSource = new Mock<ISqsMessageSource>(MockBehavior.Strict);
        sqsMessageSource.Setup(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new Message
                {
                    MessageId = messageId1,
                    ReceiptHandle = receiptHandle1,
                    Body = data1
                },
                new Message
                {
                    MessageId = messageId2,
                    ReceiptHandle = receiptHandle2,
                    Body = data2
                },
                data3Message,
                data4Message
            ]);

        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);

        var queueUrl = Guid.NewGuid().ToString();
        const int visibilityTimeoutInSeconds = 100;

        var source = new SqsJobSource(sqs.Object, sqsMessageSource.Object,
            poisonMessageHandler.Object,
            Options.Create(CreateConfig(queueUrl, visibilityTimeoutInSeconds)));

        var response = await source.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(4, response.Items.Count);

        sqs.Verify(a => a.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sqsMessageSource.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        sqsMessageSource.Verify(a => a.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken), Times.Once);

        Assert.Equal(messageId1, response.Items[0].MessageId);
        Assert.Equal(receiptHandle1, (response.Items[0] as SqsJobModel)!.RawMessage.ReceiptHandle);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(messageId2, response.Items[1].MessageId);
        Assert.Equal(receiptHandle2, (response.Items[1] as SqsJobModel)!.RawMessage.ReceiptHandle);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal(data3, response.Items[2].Body);
        Assert.Equal(data4, response.Items[3].Body);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = CreateConfig(visibilityTimeoutSeconds: 20);

        var jobSource = new SqsJobSource(null!, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            Options.Create(options));

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Empty)]
    public async Task Test_AcknowledgeAsync_IncompatibleMessage(CoreJobResult result)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        var config = CreateConfig();

        var source = new SqsJobSource(sqs.Object, null!, poisonMessageHandler.Object,
            Options.Create(config));

        var job = new OutsideContextJobModel
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        await source.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
        Assert.Empty(poisonMessageHandler.Invocations);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.Broken)]
    public async Task Test_AcknowledgeAsync_NonRecoverable_DeletesWhenNotAlreadyEnforced(
        CoreJobResult result)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse {HttpStatusCode = HttpStatusCode.OK});

        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        poisonMessageHandler
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PoisonEnforcementResult.NotEnforced);

        var config = CreateConfig();
        var source = new SqsJobSource(sqs.Object, null!, poisonMessageHandler.Object,
            Options.Create(config));

        var receiptHandle = Guid.NewGuid().ToString();
        var rawMessage = new Message {ReceiptHandle = receiptHandle};
        var job = new SqsJobModel
        {
            MessageId = receiptHandle,
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString(),
            RawMessage = rawMessage
        };

        await source.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(rawMessage, TestContext.Current.CancellationToken),
            Times.Once);
        sqs.Verify(
            s => s.DeleteMessageAsync(
                It.Is<DeleteMessageRequest>(r =>
                    r.QueueUrl == config.QueueUrl && r.ReceiptHandle == receiptHandle),
                TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.Broken)]
    public async Task Test_AcknowledgeAsync_NonRecoverable_SkipsDeleteWhenAlreadyEnforced(CoreJobResult result)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        poisonMessageHandler
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PoisonEnforcementResult.Enforced);

        var config = CreateConfig();
        var source = new SqsJobSource(sqs.Object, null!, poisonMessageHandler.Object,
            Options.Create(config));

        var rawMessage = new Message {ReceiptHandle = Guid.NewGuid().ToString()};
        var job = new SqsJobModel
        {
            MessageId = rawMessage.ReceiptHandle,
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString(),
            RawMessage = rawMessage
        };

        await source.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(rawMessage, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_RecoverableFailure_InvokesPoisonHandlerWithoutDelete(CoreJobResult result)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        poisonMessageHandler
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PoisonEnforcementResult.NotEnforced);

        var config = CreateConfig();
        var source = new SqsJobSource(sqs.Object, null!, poisonMessageHandler.Object,
            Options.Create(config));

        var rawMessage = new Message {ReceiptHandle = Guid.NewGuid().ToString()};
        var job = new SqsJobModel
        {
            MessageId = rawMessage.ReceiptHandle,
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString(),
            RawMessage = rawMessage
        };

        await source.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(rawMessage, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Success()
    {
        var sqs = new Mock<IAmazonSQS>();
        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>();
        var config = CreateConfig();

        var source = new SqsJobSource(sqs.Object, null!, poisonMessageHandler.Object,
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var receiptHandle = Guid.NewGuid().ToString();
        var job = new SqsJobModel
        {
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString(),
            RawMessage = new Message {ReceiptHandle = receiptHandle}
        };

        await source.AcknowledgeAsync(job, CoreJobResult.Success, TestContext.Current.CancellationToken);

        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), TestContext.Current.CancellationToken),
            Times.Once);

        var request = Assert.Single(sqs.Invocations).Arguments[0] as DeleteMessageRequest;
        Assert.NotNull(request);

        Assert.Equal(config.QueueUrl, request.QueueUrl);
        Assert.Equal(receiptHandle, request.ReceiptHandle);

        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<Message>(), TestContext.Current.CancellationToken),
            Times.Never);
    }

    [Theory]
    [InlineData(10, 20)]
    [InlineData(30, 30)]
    [InlineData(40, 40)]
    public async Task Test_HeartbeatAsync(int timeoutSeconds, int expectedVerified)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeMessageVisibilityResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            });

        var config = CreateConfig(visibilityTimeoutSeconds: timeoutSeconds);
        var source = new SqsJobSource(sqs.Object, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var receiptHandle = Guid.NewGuid().ToString();
        var job = new SqsJobModel
        {
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow - TimeSpan.FromMinutes(5),
            RawMessage = new Message
            {
                ReceiptHandle = receiptHandle
            },
            Body = Guid.NewGuid().ToString()
        };

        await source.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        sqs.Verify(
            s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
        sqs.Verify(
            s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(),
                TestContext.Current.CancellationToken),
            Times.Once);

        var request = Assert.Single(sqs.Invocations).Arguments[0] as ChangeMessageVisibilityRequest;
        Assert.NotNull(request);

        Assert.Equal(config.QueueUrl, request.QueueUrl);
        Assert.Equal(expectedVerified, request.VisibilityTimeout);
        Assert.Equal(receiptHandle, request.ReceiptHandle);
    }

    [Fact]
    public async Task Test_HeartbeatAsync_CanNoLongerHeartbeat()
    {
        const int timeoutSeconds = 300;

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs
            .Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            });

        var config = CreateConfig(visibilityTimeoutSeconds: timeoutSeconds);
        var source = new SqsJobSource(sqs.Object, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var receiptHandle = Guid.NewGuid().ToString();
        var job = new SqsJobModel
        {
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow - TimeSpan.FromHours(12) + TimeSpan.FromMinutes(2),
            RawMessage = new Message
            {
                ReceiptHandle = receiptHandle
            },
            Body = Guid.NewGuid().ToString()
        };

        var ex = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            source.HeartbeatAsync(job, TestContext.Current.CancellationToken));

        Assert.False(ex.IsCritical);
        Assert.False(ex.CouldBeTransient);

        sqs.Verify(
            s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
        sqs.Verify(
            s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(),
                TestContext.Current.CancellationToken),
            Times.Once);

        var request = Assert.Single(sqs.Invocations).Arguments[0] as DeleteMessageRequest;
        Assert.NotNull(request);

        Assert.Equal(config.QueueUrl, request.QueueUrl);
        Assert.Equal(receiptHandle, request.ReceiptHandle);
    }

    [Fact]
    public async Task Test_HeartbeatAsync_IncompatibleMessage()
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var config = CreateConfig(visibilityTimeoutSeconds: 30);

        var source = new SqsJobSource(sqs.Object, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            Options.Create(config));

        var job = new OutsideContextJobModel
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };

        await source.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    public class OutsideContextJobModelTests
    {
        [Fact]
        public void ImplementsIRawJobModel()
        {
            var model = new OutsideContextJobModel
            {
                MessageId = Guid.NewGuid().ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                Body = Guid.NewGuid().ToString()
            };

            Assert.IsType<IRawJobModel>(model, false);
        }

        [Fact]
        public void Properties_RoundTripAssignedValues()
        {
            var messageId = Guid.NewGuid().ToString();
            var date = DateTime.UtcNow - TimeSpan.FromDays(1);
            var body = Guid.NewGuid().ToString();

            var model = new OutsideContextJobModel
            {
                MessageId = messageId,
                CreatedAtUtc = date,
                Body = body
            };

            Assert.Equal(messageId, model.MessageId);
            Assert.Equal(messageId, model.IdempotencyId);
            Assert.Equal(date, model.CreatedAtUtc);
            Assert.Equal(body, model.Body);
        }
    }

    /// <summary>
    ///     Stand-in IRawJobModel that is not an <see cref="SqsJobModel" />, used to exercise
    ///     SqsJobSource paths that ignore messages from outside this job source.
    /// </summary>
    private class OutsideContextJobModel : IRawJobModel
    {
        public required string MessageId { get; init; }

        // ReSharper disable once ReturnTypeCanBeNotNullable
        public string? IdempotencyId => MessageId;
        public required DateTime CreatedAtUtc { get; init; }
        public required string? Body { get; init; }
    }
}