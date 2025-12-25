using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;
using System.Net;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.UnitTests.Tests.Services;

public class SqsJobSourceTests
{
    [Fact]
    public async Task TestGetJobsAsync()
    {
        const int batchSize = 10;

        var receiptHandle1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var receiptHandle2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;

        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var sqsMessageSource = new Mock<ISqsMessageSource>(MockBehavior.Strict);
        sqsMessageSource.Setup(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new Message
                {
                    ReceiptHandle = receiptHandle1,
                    Body = data1
                },
                new Message
                {
                    ReceiptHandle = receiptHandle2,
                    Body = data2
                },
                new Message
                {
                    ReceiptHandle = Guid.NewGuid().ToString(), // moot
                    Body = data3
                },
                new Message
                {
                    ReceiptHandle = Guid.NewGuid().ToString(), // moot
                    Body = data4
                }
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

        var queueUrl = Guid.NewGuid().ToString();

        const int visibilityTimeoutInSeconds = 100;

        var source = new SqsJobSource(sqs.Object, sqsMessageSource.Object, converter.Object,
            new NullLogger<SqsJobSource>(), Options.Create(new SqsConfigurationModel
            {
                QueueUrl = queueUrl,
                VisibilityTimeoutSeconds = visibilityTimeoutInSeconds
            }));

        using var cts = new CancellationTokenSource();
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

        Assert.Equal(receiptHandle1, response.Items[0].MessageId);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Equal(receiptHandle2, response.Items[1].MessageId);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = new SqsConfigurationModel
        {
            QueueUrl = null!,
            VisibilityTimeoutSeconds = 20
        };

        var jobSource = new SqsJobSource(null!, null!, null!, new NullLogger<SqsJobSource>(),
            Options.Create(options));

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        var sqs = new Mock<IAmazonSQS>();
        var config = new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            VisibilityTimeoutSeconds = 0
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(),
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns(messageId);

        await source.AcknowledgeCompletionAsync(job.Object, true, TestContext.Current.CancellationToken);

        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), TestContext.Current.CancellationToken),
            Times.Once);

        var request = Assert.Single(sqs.Invocations).Arguments[0] as DeleteMessageRequest;
        Assert.NotNull(request);

        Assert.Equal(config.QueueUrl, request.QueueUrl);
        Assert.Equal(messageId, request.ReceiptHandle);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_NonSuccess()
    {
        var sqs = new Mock<IAmazonSQS>();
        var config = new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            VisibilityTimeoutSeconds = 0
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(),
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns(messageId);

        using var cts = new CancellationTokenSource();

        await source.AcknowledgeCompletionAsync(job.Object, false, TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
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

        var config = new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(),
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.MessageId).Returns(messageId);
        job.Setup(j => j.CreatedAtUtc).Returns(DateTime.UtcNow - TimeSpan.FromMinutes(5));

        await source.HeartbeatAsync(job.Object, TestContext.Current.CancellationToken);

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
        Assert.Equal(messageId, request.ReceiptHandle);
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

        var config = new SqsConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(),
            Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.MessageId).Returns(messageId);
        job
            .Setup(j => j.CreatedAtUtc)
            .Returns(DateTime.UtcNow - TimeSpan.FromHours(12) + TimeSpan.FromMinutes(2));

        await Assert.ThrowsAsync<CanNoLongerHeartbeatException>(() =>
            source.HeartbeatAsync(job.Object, TestContext.Current.CancellationToken));

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
        Assert.Equal(messageId, request.ReceiptHandle);
    }
}