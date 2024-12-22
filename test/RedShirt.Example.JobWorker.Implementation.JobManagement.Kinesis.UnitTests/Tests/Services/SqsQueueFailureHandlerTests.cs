using Amazon.SQS;
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
}