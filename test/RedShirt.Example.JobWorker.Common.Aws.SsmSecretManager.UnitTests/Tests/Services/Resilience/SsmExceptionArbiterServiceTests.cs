using Amazon.Runtime;
using Amazon.SimpleSystemsManagement.Model;
using RedShirt.Example.JobWorker.Common.Aws.Models;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using System.Net;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.UnitTests.Tests.Services.Resilience;

public class SsmExceptionArbiterServiceTests
{
    private readonly Mock<IAwsExceptionArbiterService> _awsArbiter = new(MockBehavior.Strict);
    private readonly SsmExceptionArbiterService _sut;

    public SsmExceptionArbiterServiceTests()
    {
        _sut = new SsmExceptionArbiterService(_awsArbiter.Object);
    }

    [Fact]
    public void GetReport_AmazonServiceException_DelegatesToAwsArbiter()
    {
        var exception = new AmazonServiceException("service failure")
        {
            StatusCode = HttpStatusCode.BadGateway,
            ErrorCode = "InternalFailure"
        };
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
    public void GetReport_InternalServerErrorException_IsExpectedTransientAndExternallySolvable()
    {
        var report = _sut.GetReport(new InternalServerErrorException("ssm 500"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _awsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_InvalidParametersException_IsExpectedPermanentAndNotExternallySolvable()
    {
        var report = _sut.GetReport(new InvalidParametersException("bad params"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
        _awsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_ParameterNotFoundException_IsExpectedPermanentAndExternallySolvable()
    {
        var report = _sut.GetReport(new ParameterNotFoundException("missing"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
        _awsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new WorkerSecretManagerException("wrapped")
            {IsHandled = true, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var report = _sut.GetReport(new AggregateException(inner));

        Assert.True(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_UnrecognizedNonAwsException_IsUnexpected()
    {
        var report = _sut.GetReport(new TimeoutException("not an aws exception"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
        _awsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_WorkerSecretManagerException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerSecretManagerException("retryable")
            {IsHandled = false, CouldBeTransient = true, CouldBeExternallySolvable = true};
        var handled = new WorkerSecretManagerException("exhausted")
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
        _awsArbiter.VerifyNoOtherCalls();
    }
}