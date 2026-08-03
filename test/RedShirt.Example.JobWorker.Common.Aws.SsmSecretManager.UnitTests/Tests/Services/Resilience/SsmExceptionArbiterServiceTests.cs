using RedShirt.Example.JobWorker.Common.Aws.Models;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

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
    public void GetJudgement_DelegatesOtherExceptionsToAwsArbiter()
    {
        var exception = new TimeoutException("aws timeout");
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
    public void GetJudgement_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetJudgement(null!));
    }

    [Fact]
    public void GetJudgement_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new WorkerSecretManagerException("wrapped", false, true, true);
        var report = _sut.GetJudgement(new AggregateException(inner));

        Assert.True(report.AlreadyHandled);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_WorkerSecretManagerException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerSecretManagerException("retryable", false, true);
        var handled = new WorkerSecretManagerException("exhausted", false, true, true);

        var unhandledReport = _sut.GetJudgement(unhandled);
        var handledReport = _sut.GetJudgement(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.False(unhandledReport.IsCritical);
        Assert.True(unhandledReport.CouldBeTransient);

        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.IsCritical);
        Assert.False(handledReport.CouldBeTransient);
        _awsArbiter.VerifyNoOtherCalls();
    }
}