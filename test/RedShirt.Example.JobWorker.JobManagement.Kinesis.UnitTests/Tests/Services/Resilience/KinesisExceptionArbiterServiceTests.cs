using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using RedShirt.Example.JobWorker.Common.Aws.Models;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using DynamoDbResourceNotFoundException = Amazon.DynamoDBv2.Model.ResourceNotFoundException;
using KinesisProvisionedThroughputExceededException =
    Amazon.Kinesis.Model.ProvisionedThroughputExceededException;
using KinesisResourceNotFoundException = Amazon.Kinesis.Model.ResourceNotFoundException;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services.Resilience;

public class KinesisExceptionArbiterServiceTests
{
    private readonly Mock<IAwsExceptionArbiterService> _awsArbiter = new(MockBehavior.Strict);
    private readonly KinesisExceptionArbiterService _sut;

    public KinesisExceptionArbiterServiceTests()
    {
        _sut = new KinesisExceptionArbiterService(_awsArbiter.Object);
    }

    [Fact]
    public void GetReport_AccessDeniedException_IsCritical()
    {
        var report = _sut.GetReport(new AccessDeniedException("denied"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_AmazonDynamoDBException_DelegatesToAwsArbiter()
    {
        var exception = new AmazonDynamoDBException("generic dynamo");
        _awsArbiter.Setup(a => a.GetJudgement(exception)).Returns(new AwsExceptionArbiterReport
        {
            IsCritical = false,
            CouldBeTransient = false
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
        _awsArbiter.Verify(a => a.GetJudgement(exception), Times.Once);
    }

    [Fact]
    public void GetReport_AmazonKinesisException_DelegatesToAwsArbiter()
    {
        var exception = new AmazonKinesisException("generic kinesis");
        _awsArbiter.Setup(a => a.GetJudgement(exception)).Returns(new AwsExceptionArbiterReport
        {
            IsCritical = false,
            CouldBeTransient = true
        });

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
        _awsArbiter.Verify(a => a.GetJudgement(exception), Times.Once);
    }

    [Fact]
    public void GetReport_DynamoDbResourceNotFoundException_IsPermanentNonCritical()
    {
        var report = _sut.GetReport(new DynamoDbResourceNotFoundException("item missing"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_ExpiredIteratorException_IsPermanentNonCritical()
    {
        var report = _sut.GetReport(new ExpiredIteratorException("expired"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_KMSAccessDeniedException_IsCritical()
    {
        var report = _sut.GetReport(new KMSAccessDeniedException("kms denied"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_KMSDisabledException_IsTransient()
    {
        var report = _sut.GetReport(new KMSDisabledException("key disabled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_KinesisResourceNotFoundException_IsCritical()
    {
        var report = _sut.GetReport(new KinesisResourceNotFoundException("missing stream"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_ProvisionedThroughputExceeded_IsTransient()
    {
        var report = _sut.GetReport(new KinesisProvisionedThroughputExceededException("throttled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var report = _sut.GetReport(new AggregateException(new AccessDeniedException("denied")));

        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TableNotFoundException_IsCritical()
    {
        var report = _sut.GetReport(new TableNotFoundException("missing table"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsCritical()
    {
        var report = _sut.GetReport(new InvalidOperationException("boom"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
        _awsArbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetReport_WorkerDistributedException_IsAlreadyHandled()
    {
        var exception = new WorkerDistributedException("redis failure", false, true);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerJobSourceException("retryable", false, true);
        var handled = new WorkerJobSourceException("exhausted", false, true, true);

        var unhandledReport = _sut.GetReport(unhandled);
        var handledReport = _sut.GetReport(handled);

        Assert.True(unhandledReport.AlreadyHandled);
        Assert.True(unhandledReport.CouldBeTransient);

        Assert.True(handledReport.AlreadyHandled);
        Assert.False(handledReport.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerSqsException_Handled_RespectsIsHandled()
    {
        var unhandled = new WorkerSqsException("retryable", false, true);
        var handled = new WorkerSqsException("exhausted", false, true, true);

        Assert.True(_sut.GetReport(unhandled).CouldBeTransient);
        Assert.False(_sut.GetReport(handled).CouldBeTransient);
        Assert.True(_sut.GetReport(unhandled).AlreadyHandled);
    }
}