using Amazon.Runtime;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using System.Net;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Aws.UnitTests.Tests.Services.Resilience;

public class AwsExceptionArbiterServiceTests
{
    private readonly AwsExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_AmazonClientException_IsTransient()
    {
        var report = _sut.GetReport(new AmazonClientException("client failure"));

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_AmazonServiceException_WithPermanentStatus_IsNotTransient()
    {
        var exception = new AmazonServiceException("denied", ErrorType.Sender, "AccessDenied", "req",
            HttpStatusCode.Forbidden);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData("Throttling")]
    [InlineData("ThrottlingException")]
    [InlineData("RequestLimitExceeded")]
    [InlineData("SlowDown")]
    [InlineData("InternalFailure")]
    public void GetReport_AmazonServiceException_WithTransientErrorCode_IsTransient(string errorCode)
    {
        var exception = new AmazonServiceException("transient")
        {
            ErrorCode = errorCode,
            StatusCode = HttpStatusCode.BadRequest
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void GetReport_AmazonServiceException_WithTransientStatus_IsTransient(HttpStatusCode statusCode)
    {
        var exception = new AmazonServiceException("transient status", ErrorType.Receiver, "Other", "req", statusCode);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_ArgumentException_IsNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad arg", "name"));

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_HttpRequestException_IsTransient()
    {
        var report = _sut.GetReport(new HttpRequestException("connection reset"));

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotExpected()
    {
        var report = _sut.GetReport(new AggregateException(
            new SocketException((int) SocketError.TimedOut),
            new HttpRequestException("also failed")));

        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException());

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var report = _sut.GetReport(new AggregateException(new SocketException((int) SocketError.TimedOut)));

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SocketException_IsTransient()
    {
        var report = _sut.GetReport(new SocketException((int) SocketError.TimedOut));

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException());

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsNotExpected()
    {
        var report = _sut.GetReport(new InvalidOperationException("boom"));

        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }
}