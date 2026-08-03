using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class SqsQueueFailureHandlerTests
{
    private static Mock<IRawJobModel> CreateRawJobModel(string? body = null)
    {
        var mock = new Mock<IRawJobModel>(MockBehavior.Strict);
        mock.SetupGet(m => m.Body).Returns(body ?? Guid.NewGuid().ToString());
        return mock;
    }

    [Fact]
    public async Task SendFailure_NoQueueUrl()
    {
        var sqs = new Mock<IAmazonSQS>();
        var sender = new SqsQueueFailureHandler(sqs.Object, Options.Create(new SqsQueueFailureHandler.ConfigurationModel
        {
            QueueUrl = null!
        }));

        await sender.HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
            TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    [Fact]
    public async Task SendFailure_QueueUrl()
    {
        var sqs = new Mock<IAmazonSQS>();
        var body = Guid.NewGuid().ToString();
        var sender = new SqsQueueFailureHandler(sqs.Object, Options.Create(new SqsQueueFailureHandler.ConfigurationModel
        {
            QueueUrl = "foo"
        }));

        await sender.HandleFailureAsync(CreateRawJobModel(body).Object, FailureType.Execution, null,
            TestContext.Current.CancellationToken);

        Assert.Single(sqs.Invocations);
        sqs.Verify(a => a.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        sqs.Verify(
            a => a.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "foo" && r.MessageBody == body),
                TestContext.Current.CancellationToken), Times.Once);
    }
}