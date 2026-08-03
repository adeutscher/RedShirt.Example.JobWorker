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
    public void GetJudgement_AmazonSQSException_DelegatesToAwsArbiter()
    {
        var exception = new AmazonSQSException("generic sqs");
        _awsArbiter.Setup(a => a.GetJudgement(exception)).Returns(new AwsExceptionArbiterReport
        {
            IsCritical = false,
            CouldBeTransient = true
        });

        var report = _sut.GetJudgement(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
        _awsArbiter.Verify(a => a.GetJudgement(exception), Times.Once);
    }

    [Fact]
    public void GetJudgement_AmazonServiceException_IsCriticalAndNotTransient()
    {
        var exception = new AmazonServiceException("generic aws service failure");

        var report = _sut.GetJudgement(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
        _awsArbiter.Verify(a => a.GetJudgement(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public void GetJudgement_KmsDisabledException_IsTransient()
    {
        var report = _sut.GetJudgement(new KmsDisabledException("key disabled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetJudgement(null!));
    }

    [Fact]
    public void GetJudgement_QueueDoesNotExistException_IsPermanentNonCritical()
    {
        var report = _sut.GetJudgement(new QueueDoesNotExistException("missing"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_RequestThrottledException_IsTransient()
    {
        var report = _sut.GetJudgement(new RequestThrottledException("throttled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var report = _sut.GetJudgement(new AggregateException(new RequestThrottledException("throttled")));

        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_UnrecognizedException_IsCritical()
    {
        var report = _sut.GetJudgement(new InvalidOperationException("boom"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_WorkerSqsException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerSqsException("retryable", false, true);
        var handled = new WorkerSqsException("exhausted", false, true, true);

        var unhandledReport = _sut.GetJudgement(unhandled);
        var handledReport = _sut.GetJudgement(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.True(unhandledReport.CouldBeTransient);
        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.CouldBeTransient);
    }
}