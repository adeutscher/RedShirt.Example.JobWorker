using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.UnitTests.Tests.Services;

public class SqsJobSourceTests
{
    [Fact]
    public async Task TestGetJobsAsync()
    {
        var receiptHandle1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var receiptHandle2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;

        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        sqs.Setup(a => a.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ReceiveMessageResponse
            {
                Messages =
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
                ]
            });

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1))
            .Returns(mock1);
        converter.Setup(c => c.Convert(data2))
            .Returns(mock2);
        converter.Setup(c => c.Convert(data3))
            .Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data4))
            .Returns((string _) => throw new Exception());
        var sorter = new Mock<ISourceMessageSorter>();
        sorter.Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()))
            .Returns((List<IJobModel> input) => input);

        var queueUrl = Guid.NewGuid().ToString();
        const int batchSize = 10;
        const int visibilityTimeoutInSeconds = 100;

        var source = new SqsJobSource(sqs.Object, converter.Object, sorter.Object,
            new NullLogger<SqsJobSource>(), Options.Create(new SqsJobSource.ConfigurationModel
            {
                QueueUrl = queueUrl,
                MessageBatchSize = batchSize,
                VisibilityTimeoutSeconds = visibilityTimeoutInSeconds
            }));

        var cts = new CancellationTokenSource();
        var response = await source.GetJobsAsync(cts.Token);
        Assert.Equal(2, response.Items.Count);

        sqs.Verify(a => a.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sqs.Verify(
            a => a.ReceiveMessageAsync(
                It.Is<ReceiveMessageRequest>(r =>
                    r.QueueUrl == queueUrl
                    && r.MaxNumberOfMessages == batchSize
                    && r.VisibilityTimeout == visibilityTimeoutInSeconds),
                cts.Token), Times.Once);

        converter.Verify(c => c.Convert(data1), Times.Once);
        converter.Verify(c => c.Convert(data2), Times.Once);
        converter.Verify(c => c.Convert(data3), Times.Once);
        converter.Verify(c => c.Convert(data4), Times.Once);
        sorter.Verify(a => a.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()), Times.Once);

        Assert.Equal(receiptHandle1, response.Items[0].MessageId);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Equal(receiptHandle2, response.Items[1].MessageId);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        var sqs = new Mock<IAmazonSQS>();
        var config = new SqsJobSource.ConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            MessageBatchSize = 0,
            VisibilityTimeoutSeconds = 0
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(), Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns(messageId);

        var cts = new CancellationTokenSource();

        await source.AcknowledgeCompletionAsync(job.Object, true, cts.Token);

        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), cts.Token), Times.Once);

        var request = Assert.Single(sqs.Invocations).Arguments[0] as DeleteMessageRequest;
        Assert.NotNull(request);

        Assert.Equal(config.QueueUrl, request.QueueUrl);
        Assert.Equal(messageId, request.ReceiptHandle);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_NonSuccess()
    {
        var sqs = new Mock<IAmazonSQS>();
        var config = new SqsJobSource.ConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            MessageBatchSize = 0,
            VisibilityTimeoutSeconds = 0
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(), Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns(messageId);

        var cts = new CancellationTokenSource();

        await source.AcknowledgeCompletionAsync(job.Object, false, cts.Token);

        Assert.Empty(sqs.Invocations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Test_HeartbeatAsync(int timeoutSeconds)
    {
        var sqs = new Mock<IAmazonSQS>();
        var config = new SqsJobSource.ConfigurationModel
        {
            QueueUrl = Guid.NewGuid()
                .ToString(),
            MessageBatchSize = 0,
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var source = new SqsJobSource(sqs.Object, null!, null!, new NullLogger<SqsJobSource>(), Options.Create(config));

        var messageId = Guid.NewGuid().ToString();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns(messageId);

        var cts = new CancellationTokenSource();

        await source.HeartbeatAsync(job.Object, cts.Token);

        sqs.Verify(
            s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
        sqs.Verify(s => s.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), cts.Token),
            Times.Once);

        var request = Assert.Single(sqs.Invocations).Arguments[0] as ChangeMessageVisibilityRequest;
        Assert.NotNull(request);

        Assert.Equal(config.QueueUrl, request.QueueUrl);
        Assert.Equal(config.VisibilityTimeoutSeconds, request.VisibilityTimeout);
        Assert.Equal(messageId, request.ReceiptHandle);
    }
}