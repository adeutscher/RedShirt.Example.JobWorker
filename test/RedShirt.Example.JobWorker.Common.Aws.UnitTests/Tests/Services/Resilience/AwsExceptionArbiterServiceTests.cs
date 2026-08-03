using Amazon.Runtime;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using System.Net;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Aws.UnitTests.Tests.Services.Resilience;

public class AwsExceptionArbiterServiceTests
{
    private readonly AwsExceptionArbiterService _sut = new();

    [Fact]
    public void GetJudgement_AmazonClientException_IsTransient()
    {
        var judgement = _sut.GetJudgement(new AmazonClientException("client failure"));

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_AmazonServiceException_WithPermanentStatus_IsNotTransient()
    {
        var exception = new AmazonServiceException("denied", ErrorType.Sender, "AccessDenied", "req",
            HttpStatusCode.Forbidden);

        var judgement = _sut.GetJudgement(exception);

        Assert.False(judgement.IsCritical);
        Assert.False(judgement.CouldBeTransient);
    }

    [Theory]
    [InlineData("Throttling")]
    [InlineData("ThrottlingException")]
    [InlineData("RequestLimitExceeded")]
    [InlineData("SlowDown")]
    [InlineData("InternalFailure")]
    public void GetJudgement_AmazonServiceException_WithTransientErrorCode_IsTransient(string errorCode)
    {
        var exception = new AmazonServiceException("transient")
        {
            ErrorCode = errorCode,
            StatusCode = HttpStatusCode.BadRequest
        };

        var judgement = _sut.GetJudgement(exception);

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void GetJudgement_AmazonServiceException_WithTransientStatus_IsTransient(HttpStatusCode statusCode)
    {
        var exception = new AmazonServiceException("transient status", ErrorType.Receiver, "Other", "req", statusCode);

        var judgement = _sut.GetJudgement(exception);

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_ArgumentException_IsNotTransient()
    {
        var judgement = _sut.GetJudgement(new ArgumentException("bad arg", "name"));

        Assert.False(judgement.IsCritical);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_HttpRequestException_IsTransient()
    {
        var judgement = _sut.GetJudgement(new HttpRequestException("connection reset"));

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_MultiInnerAggregateException_IsCritical()
    {
        var judgement = _sut.GetJudgement(new AggregateException(
            new SocketException((int) SocketError.TimedOut),
            new HttpRequestException("also failed")));

        Assert.True(judgement.IsCritical);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetJudgement(null!));
    }

    [Fact]
    public void GetJudgement_OperationCanceledException_IsNotTransient()
    {
        var judgement = _sut.GetJudgement(new OperationCanceledException());

        Assert.False(judgement.IsCritical);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var judgement = _sut.GetJudgement(new AggregateException(new SocketException((int) SocketError.TimedOut)));

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_SocketException_IsTransient()
    {
        var judgement = _sut.GetJudgement(new SocketException((int) SocketError.TimedOut));

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_TaskCanceledException_IsTransient()
    {
        var judgement = _sut.GetJudgement(new TaskCanceledException());

        Assert.False(judgement.IsCritical);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_UnrecognizedException_IsCritical()
    {
        var judgement = _sut.GetJudgement(new InvalidOperationException("boom"));

        Assert.True(judgement.IsCritical);
        Assert.False(judgement.CouldBeTransient);
    }
}