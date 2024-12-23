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
}