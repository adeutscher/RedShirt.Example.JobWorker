using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
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
        var mock1 = new Mock<IJobDataModel>().Object;
        var messageId2 = Guid.NewGuid().ToString();
        var receiptHandle2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;

        var data3 = Guid.NewGuid().ToString();
        var data3PoisonMessage = new Message
        {
            ReceiptHandle = Guid.NewGuid().ToString(),
            Body = data3
        };
        var data4 = Guid.NewGuid().ToString();
        var data4PoisonMessage = new Message
        {
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
                data3PoisonMessage,
                data4PoisonMessage
            ]);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1))
            .Returns(mock1);
        converter.Setup(c => c.Convert(data2))
            .Returns(mock2);
        converter.Setup(c => c.Convert(data3))
            .Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data4))
            .Returns((string _) => throw new Exception());

        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        poisonMessageHandler
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(data3PoisonMessage, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        poisonMessageHandler
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(data4PoisonMessage, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var queueUrl = Guid.NewGuid().ToString();
        const int visibilityTimeoutInSeconds = 100;

        var source = new SqsJobSource(sqs.Object, sqsMessageSource.Object, converter.Object,
            poisonMessageHandler.Object, new NullLogger<SqsJobSource>(),
            Options.Create(CreateConfig(queueUrl, visibilityTimeoutInSeconds)));

        var response = await source.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        sqs.Verify(a => a.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sqsMessageSource.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        sqsMessageSource.Verify(a => a.GetMessagesAsync(batchSize, TestContext.Current.CancellationToken), Times.Once);

        converter.Verify(c => c.Convert(data1), Times.Once);
        converter.Verify(c => c.Convert(data2), Times.Once);
        converter.Verify(c => c.Convert(data3), Times.Once);
        converter.Verify(c => c.Convert(data4), Times.Once);
        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<Message>(), TestContext.Current.CancellationToken),
            Times.Exactly(2));
        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(data3PoisonMessage, TestContext.Current.CancellationToken),
            Times.Once);
        poisonMessageHandler.Verify(
            p => p.AttemptPoisonMessageEnforcementAsync(data4PoisonMessage, TestContext.Current.CancellationToken),
            Times.Once);

        Assert.Equal(messageId1, response.Items[0].MessageId);
        Assert.Equal(receiptHandle1, (response.Items[0] as SqsJobModel)!.RawMessage.ReceiptHandle);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Equal(messageId2, response.Items[1].MessageId);
        Assert.Equal(receiptHandle2, (response.Items[1] as SqsJobModel)!.RawMessage.ReceiptHandle);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = CreateConfig(visibilityTimeoutSeconds: 20);

        var jobSource = new SqsJobSource(null!, null!, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            new NullLogger<SqsJobSource>(), Options.Create(options));

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Test_AcknowledgeAsync_IncompatibleMessage(bool success)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        var config = CreateConfig();

        var source = new SqsJobSource(sqs.Object, null!, null!, poisonMessageHandler.Object,
            new NullLogger<SqsJobSource>(), Options.Create(config));

        var job = new OutsideContextJobModel
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        await source.AcknowledgeCompletionAsync(job, success, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
        Assert.Empty(poisonMessageHandler.Invocations);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_NonSuccess()
    {
        var sqs = new Mock<IAmazonSQS>();
        var poisonMessageHandler = new Mock<ISqsPoisonMessagesHandler>(MockBehavior.Strict);
        poisonMessageHandler
            .Setup(p => p.AttemptPoisonMessageEnforcementAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = CreateConfig();
        var source = new SqsJobSource(sqs.Object, null!, null!, poisonMessageHandler.Object,
            new NullLogger<SqsJobSource>(), Options.Create(config));

        var rawMessage = new Message {ReceiptHandle = Guid.NewGuid().ToString()};
        var job = new SqsJobModel
        {
            MessageId = rawMessage.ReceiptHandle,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object,
            RawMessage = rawMessage
        };

        await source.AcknowledgeCompletionAsync(job, false, TestContext.Current.CancellationToken);

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

        var source = new SqsJobSource(sqs.Object, null!, null!, poisonMessageHandler.Object,
            new NullLogger<SqsJobSource>(), Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var receiptHandle = Guid.NewGuid().ToString();
        var job = new SqsJobModel
        {
            MessageId = messageId,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object,
            RawMessage = new Message {ReceiptHandle = receiptHandle}
        };

        await source.AcknowledgeCompletionAsync(job, true, TestContext.Current.CancellationToken);

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
        var source = new SqsJobSource(sqs.Object, null!, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            new NullLogger<SqsJobSource>(), Options.Create(config));

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
            Data = null!
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
        var source = new SqsJobSource(sqs.Object, null!, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            new NullLogger<SqsJobSource>(), Options.Create(config));

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
            Data = null!
        };

        await Assert.ThrowsAsync<CanNoLongerHeartbeatException>(() =>
            source.HeartbeatAsync(job, TestContext.Current.CancellationToken));

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

        var source = new SqsJobSource(sqs.Object, null!, null!, Mock.Of<ISqsPoisonMessagesHandler>(),
            new NullLogger<SqsJobSource>(), Options.Create(config));

        var job = new OutsideContextJobModel
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        await source.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    public class OutsideContextJobModelTests
    {
        [Fact]
        public void ImplementsIJobModel()
        {
            var model = new OutsideContextJobModel
            {
                MessageId = Guid.NewGuid().ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                Data = new Mock<IJobDataModel>().Object
            };

            Assert.IsAssignableFrom<IJobModel>(model);
        }

        [Fact]
        public void Properties_RoundTripAssignedValues()
        {
            var messageId = Guid.NewGuid().ToString();
            var date = DateTime.UtcNow - TimeSpan.FromDays(1);
            var data = new Mock<IJobDataModel>(MockBehavior.Strict);

            var model = new OutsideContextJobModel
            {
                MessageId = messageId,
                CreatedAtUtc = date,
                Data = data.Object
            };

            Assert.Equal(messageId, model.MessageId);
            Assert.Equal(messageId, model.IdempotencyId);
            Assert.Equal(date, model.CreatedAtUtc);
            Assert.Same(data.Object, model.Data);
        }
    }

    /// <summary>
    ///     Stand-in IJobModel that is not an <see cref="SqsJobModel" />, used to exercise
    ///     SqsJobSource paths that ignore messages from outside this job source.
    /// </summary>
    private class OutsideContextJobModel : IJobModel
    {
        public required string MessageId { get; init; }
        public string? IdempotencyId => MessageId;
        public required DateTime CreatedAtUtc { get; init; }
        public required IJobDataModel Data { get; init; }
    }
}