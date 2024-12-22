using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class SqsQueueFailureHandlerTests
{
    [Fact]
    public async Task SendFailure_NoQueueUrl()
    {
        var sqs = new Mock<IAmazonSQS>();
        var sender = new SqsQueueFailureHandler(sqs.Object, Options.Create(new SqsQueueFailureHandler.ConfigurationModel
        {
            QueueUrl = null!
        }));

        var cts = new CancellationTokenSource();
        await sender.HandleFailureAsync(new Mock<IJobModel>().Object, null!, cts.Token);

        Assert.Empty(sqs.Invocations);
    }

    [Fact]
    public async Task SendFailure_QueueUrl()
    {
        var sqs = new Mock<IAmazonSQS>();
        var sender = new SqsQueueFailureHandler(sqs.Object, Options.Create(new SqsQueueFailureHandler.ConfigurationModel
        {
            QueueUrl = "foo"
        }));

        var cts = new CancellationTokenSource();
        await sender.HandleFailureAsync(new Mock<IJobModel>().Object, null!, cts.Token);

        Assert.Single(sqs.Invocations);
        sqs.Verify(a => a.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        sqs.Verify(a => a.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "foo"), cts.Token), Times.Once);
    }
}