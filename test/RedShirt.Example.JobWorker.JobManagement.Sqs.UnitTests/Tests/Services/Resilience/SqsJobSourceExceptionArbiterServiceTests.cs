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
    public void GetReport_DelegatesAlreadyHandledFromSqsArbiter()
    {
        var exception = new InvalidOperationException("already classified");
        _sqsArbiter.Setup(a => a.GetReport(exception)).Returns(new SqsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        });

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_DelegatesOtherExceptionsToSqsArbiter_AsFresh()
    {
        var exception = new TimeoutException("sqs timeout");
        _sqsArbiter.Setup(a => a.GetReport(exception)).Returns(new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _sqsArbiter.Verify(a => a.GetReport(exception), Times.Once);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new WorkerSqsException("wrapped")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var report = _sut.GetReport(new AggregateException(inner));

        Assert.True(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _sqsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_UnexpectedFromSqsArbiter_IsNotExpected()
    {
        var exception = new InvalidOperationException("unknown");
        _sqsArbiter.Setup(a => a.GetReport(exception)).Returns(new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerJobSourceException("retryable")
            {IsHandled = false, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var handled = new WorkerJobSourceException("exhausted")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = false};

        var unhandledReport = _sut.GetReport(unhandled);
        var handledReport = _sut.GetReport(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.True(unhandledReport.IsExpected);
        Assert.True(unhandledReport.CouldBeTransient);
        Assert.True(unhandledReport.CouldBeExternallySolvable);

        Assert.True(handledReport.AlreadyHandled);
        Assert.True(handledReport.IsExpected);
        Assert.False(handledReport.CouldBeTransient);
        Assert.False(handledReport.CouldBeExternallySolvable);
        _sqsArbiter.VerifyNoOtherCalls();
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
        _sqsArbiter.VerifyNoOtherCalls();
    }
}