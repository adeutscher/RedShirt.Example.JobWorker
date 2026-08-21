using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.Models;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;

public class AzureServiceBusExceptionArbiterServiceTests
{
    private readonly Mock<IAzureExceptionArbiterService> _azureArbiter = new(MockBehavior.Strict);
    private readonly AzureServiceBusExceptionArbiterService _sut;

    public AzureServiceBusExceptionArbiterServiceTests()
    {
        _sut = new AzureServiceBusExceptionArbiterService(_azureArbiter.Object);
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
    public void GetReport_ServiceBusException_GeneralError_HonoursIsTransient()
    {
        var transient = new ServiceBusException(true, "general", reason: ServiceBusFailureReason.GeneralError);
        var permanent = new ServiceBusException(false, "general", reason: ServiceBusFailureReason.GeneralError);

        var transientReport = _sut.GetReport(transient);
        var permanentReport = _sut.GetReport(permanent);

        Assert.False(transientReport.AlreadyHandled);
        Assert.True(transientReport.IsExpected);
        Assert.True(transientReport.CouldBeTransient);
        Assert.True(transientReport.CouldBeExternallySolvable);

        Assert.True(permanentReport.IsExpected);
        Assert.False(permanentReport.CouldBeTransient);
        Assert.False(permanentReport.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.MessagingEntityNotFound)]
    [InlineData(ServiceBusFailureReason.MessagingEntityDisabled)]
    public void GetReport_ServiceBusException_PermanentExternallySolvableReasons(
        ServiceBusFailureReason reason)
    {
        var exception = new ServiceBusException("missing entity", reason);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.MessageLockLost)]
    [InlineData(ServiceBusFailureReason.MessageNotFound)]
    [InlineData(ServiceBusFailureReason.MessageSizeExceeded)]
    [InlineData(ServiceBusFailureReason.SessionCannotBeLocked)]
    [InlineData(ServiceBusFailureReason.SessionLockLost)]
    [InlineData(ServiceBusFailureReason.MessagingEntityAlreadyExists)]
    public void GetReport_ServiceBusException_PermanentReasons_AreExpectedAndNotTransient(
        ServiceBusFailureReason reason)
    {
        var exception = new ServiceBusException("permanent", reason);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ServiceBusFailureReason.ServiceTimeout)]
    [InlineData(ServiceBusFailureReason.ServiceBusy)]
    [InlineData(ServiceBusFailureReason.ServiceCommunicationProblem)]
    [InlineData(ServiceBusFailureReason.QuotaExceeded)]
    public void GetReport_ServiceBusException_TransientReasons_AreExpectedAndTransient(
        ServiceBusFailureReason reason)
    {
        var exception = new ServiceBusException("transient", reason);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _azureArbiter.VerifyNoOtherCalls();
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
    public void GetReport_UnauthorizedAccessException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new UnauthorizedAccessException("not authorized"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
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
}