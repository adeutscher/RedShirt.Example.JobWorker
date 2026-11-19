using Amazon.SQS.Model;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.UnitTests.Tests.Services.Resilience;

public class KafkaFailureQueueRetryWrapperServiceTests
{
    private static Mock<ISqsRetryWrapperService> CreateSqsWrapper()
    {
        var sqsWrapper = new Mock<ISqsRetryWrapperService>(MockBehavior.Strict);
        sqsWrapper
            .Setup(w => w.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> func, CancellationToken token) => func(token));
        return sqsWrapper;
    }

    [Fact]
    public async Task RunAsync_Success_RunsThroughSqsWrapper()
    {
        var sqsWrapper = CreateSqsWrapper();
        var ran = false;

        await new KafkaFailureQueueRetryWrapperService(sqsWrapper.Object).RunAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.True(ran);
        sqsWrapper.Verify(
            w => w.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task RunAsync_WorkerSqsException_RethrownAsJobSourceExceptionWithSameFlags(bool isHandled,
        bool couldBeTransient, bool couldBeExternallySolvable)
    {
        var failure = new WorkerSqsException(new QueueDoesNotExistException("missing"))
        {
            IsHandled = isHandled,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            new KafkaFailureQueueRetryWrapperService(CreateSqsWrapper().Object)
                .RunAsync(_ => Task.FromException(failure), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown.InnerException);
        Assert.Equal(isHandled, thrown.IsHandled);
        Assert.Equal(couldBeTransient, thrown.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, thrown.CouldBeExternallySolvable);
    }

    [Fact]
    public async Task RunAsync_UnclassifiedException_StaysRaw()
    {
        var unexpected = new InvalidOperationException("unexpected");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KafkaFailureQueueRetryWrapperService(CreateSqsWrapper().Object)
                .RunAsync(_ => Task.FromException(unexpected), TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
    }
}
