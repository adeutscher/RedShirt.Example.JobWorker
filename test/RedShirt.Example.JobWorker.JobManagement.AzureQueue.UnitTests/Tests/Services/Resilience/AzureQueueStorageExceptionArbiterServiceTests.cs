using Azure;
using Azure.Storage.Queues.Models;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.Models;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Services.Resilience;

public class AzureQueueStorageExceptionArbiterServiceTests
{
    private readonly Mock<IAzureExceptionArbiterService> _azureArbiter = new(MockBehavior.Strict);
    private readonly AzureQueueStorageExceptionArbiterService _sut;

    public AzureQueueStorageExceptionArbiterServiceTests()
    {
        _sut = new AzureQueueStorageExceptionArbiterService(_azureArbiter.Object);
    }

    [Fact]
    public void GetReport_DelegatesOtherExceptionsToAzureArbiter_AsFresh()
    {
        var exception = new HttpRequestException("connection reset");
        _azureArbiter.Setup(a => a.GetReport(exception)).Returns(new AzureExceptionArbiterReport
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
        _azureArbiter.Verify(a => a.GetReport(exception), Times.Once);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_RequestFailedException_InternalError_IsTransient()
    {
        var exception = new RequestFailedException(500, "internal", QueueErrorCode.InternalError.ToString(), null);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(MessageLevelErrorCodes))]
    public void GetReport_RequestFailedException_MessageLevelErrors_AreExpectedAndNotExternallySolvable(
        string errorCode)
    {
        var exception = new RequestFailedException(404, "queue message error", errorCode, null);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(QueueLevelErrorCodes))]
    public void GetReport_RequestFailedException_QueueLevelErrors_AreExpectedAndExternallySolvable(string errorCode)
    {
        var exception = new RequestFailedException(404, "queue error", errorCode, null);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_RequestFailedException_UnknownCode_DelegatesToAzureArbiter()
    {
        var exception = new RequestFailedException(429, "throttled", "UnknownCode", null);
        _azureArbiter.Setup(a => a.GetReport(exception)).Returns(new AzureExceptionArbiterReport
        {
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        _azureArbiter.Verify(a => a.GetReport(exception), Times.Once);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new WorkerAzureException("wrapped")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var report = _sut.GetReport(new AggregateException(inner));

        Assert.True(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_UnexpectedFromAzureArbiter_IsNotExpected()
    {
        var exception = new InvalidOperationException("unknown");
        _azureArbiter.Setup(a => a.GetReport(exception)).Returns(new AzureExceptionArbiterReport
        {
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
    public void GetReport_WorkerAzureException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerAzureException("retryable")
            {IsHandled = false, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var handled = new WorkerAzureException("exhausted")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = false};

        var unhandledReport = _sut.GetReport(unhandled);
        var handledReport = _sut.GetReport(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.True(unhandledReport.CouldBeTransient);
        Assert.True(unhandledReport.CouldBeExternallySolvable);
        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.CouldBeTransient);
        Assert.False(handledReport.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
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
        _azureArbiter.VerifyNoOtherCalls();
    }

    public static TheoryData<string> MessageLevelErrorCodes()
    {
        return
        [
            QueueErrorCode.MessageNotFound.ToString(),
            QueueErrorCode.PopReceiptMismatch.ToString(),
            QueueErrorCode.MessageTooLarge.ToString()
        ];
    }

    public static TheoryData<string> QueueLevelErrorCodes()
    {
        return
        [
            QueueErrorCode.QueueNotFound.ToString(),
            QueueErrorCode.QueueBeingDeleted.ToString(),
            QueueErrorCode.QueueDisabled.ToString(),
            QueueErrorCode.AuthorizationFailure.ToString(),
            QueueErrorCode.AuthenticationFailed.ToString()
        ];
    }
}