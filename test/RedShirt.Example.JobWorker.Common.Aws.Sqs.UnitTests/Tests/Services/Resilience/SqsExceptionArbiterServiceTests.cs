using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Moq;
using RedShirt.Example.JobWorker.Common.Aws.Models;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.UnitTests.Tests.Services.Resilience;

public class SqsExceptionArbiterServiceTests
{
    private readonly Mock<IAwsExceptionArbiterService> _awsArbiter = new(MockBehavior.Strict);
    private readonly SqsExceptionArbiterService _sut;

    public SqsExceptionArbiterServiceTests()
    {
        _sut = new SqsExceptionArbiterService(_awsArbiter.Object);
    }

    [Fact]
    public void GetReport_AmazonSQSException_DelegatesToAwsArbiter()
    {
        var exception = new AmazonSQSException("generic sqs");
        _awsArbiter.Setup(a => a.GetReport(exception)).Returns(new AwsExceptionArbiterReport
        {
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _awsArbiter.Verify(a => a.GetReport(exception), Times.Once);
    }

    [Fact]
    public void GetReport_AmazonServiceException_IsNotExpectedAndNotTransient()
    {
        var exception = new AmazonServiceException("generic aws service failure");

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        _awsArbiter.Verify(a => a.GetReport(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public void GetReport_KmsDisabledException_IsTransient()
    {
        var report = _sut.GetReport(new KmsDisabledException("key disabled"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_QueueDoesNotExistException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new QueueDoesNotExistException("missing"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_RequestThrottledException_IsTransient()
    {
        var report = _sut.GetReport(new RequestThrottledException("throttled"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var report = _sut.GetReport(new AggregateException(new RequestThrottledException("throttled")));

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsNotExpected()
    {
        var report = _sut.GetReport(new InvalidOperationException("boom"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerSqsException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerSqsException("retryable")
            {IsHandled = false, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var handled = new WorkerSqsException("exhausted")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = false};

        var unhandledReport = _sut.GetReport(unhandled);
        var handledReport = _sut.GetReport(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.True(unhandledReport.CouldBeTransient);
        Assert.True(unhandledReport.CouldBeExternallySolvable);
        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.CouldBeTransient);
        Assert.False(handledReport.CouldBeExternallySolvable);
    }
}