using Moq;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Models;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.UnitTests.Tests.Services.Resilience;

public class SqsJobSourceExceptionArbiterServiceTests
{
    private readonly Mock<ISqsExceptionArbiterService> _sqsArbiter = new(MockBehavior.Strict);
    private readonly SqsJobSourceExceptionArbiterService _sut;

    public SqsJobSourceExceptionArbiterServiceTests()
    {
        _sut = new SqsJobSourceExceptionArbiterService(_sqsArbiter.Object);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerJobSourceException("retryable", false, true);
        var handled = new WorkerJobSourceException("exhausted", false, true, true);

        var unhandledReport = _sut.GetReport(unhandled);
        var handledReport = _sut.GetReport(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.False(unhandledReport.IsCritical);
        Assert.True(unhandledReport.CouldBeTransient);

        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.IsCritical);
        Assert.False(handledReport.CouldBeTransient);
        _sqsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_WorkerSqsException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerSqsException("retryable", false, true);
        var handled = new WorkerSqsException("exhausted", false, true, true);

        var unhandledReport = _sut.GetReport(unhandled);
        var handledReport = _sut.GetReport(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.True(unhandledReport.CouldBeTransient);
        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.CouldBeTransient);
        _sqsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_DelegatesOtherExceptionsToSqsArbiter_AsFresh()
    {
        var exception = new TimeoutException("sqs timeout");
        _sqsArbiter.Setup(a => a.GetJudgement(exception)).Returns(new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = false,
            CouldBeTransient = true
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
        _sqsArbiter.Verify(a => a.GetJudgement(exception), Times.Once);
    }

    [Fact]
    public void GetReport_DelegatesAlreadyHandledFromSqsArbiter()
    {
        var exception = new InvalidOperationException("already classified");
        _sqsArbiter.Setup(a => a.GetJudgement(exception)).Returns(new SqsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = false,
            CouldBeTransient = false
        });

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_CriticalFromSqsArbiter_IsCritical()
    {
        var exception = new InvalidOperationException("unknown");
        _sqsArbiter.Setup(a => a.GetJudgement(exception)).Returns(new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = true,
            CouldBeTransient = false
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new WorkerSqsException("wrapped", false, true, true);
        var report = _sut.GetReport(new AggregateException(inner));

        Assert.True(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
        _sqsArbiter.VerifyNoOtherCalls();
    }
}
