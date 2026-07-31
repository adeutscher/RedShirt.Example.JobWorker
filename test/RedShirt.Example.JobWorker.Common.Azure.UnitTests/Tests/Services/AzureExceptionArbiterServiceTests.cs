using Azure;
using Azure.Core;
using Azure.Identity;
using RedShirt.Example.JobWorker.Common.Azure.Services;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Services;

public class AzureExceptionArbiterServiceTests
{
    private readonly AzureExceptionArbiterService _sut = new();

    [Fact]
    public void GetJudgement_ArgumentException_IsExpectedAndNotTransient()
    {
        var exception = new ArgumentException("secret name is empty", "name");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_ArgumentNullException_IsExpectedAndNotTransient()
    {
        var exception = new ArgumentNullException("name");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_AuthenticationFailedException_IsExpectedAndTransient()
    {
        var exception = new AuthenticationFailedException("token acquisition failed");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_AuthenticationRequiredException_IsExpectedAndNotTransient()
    {
        var exception = new AuthenticationRequiredException(
            "interactive auth required",
            new TokenRequestContext(["https://vault.azure.net/.default"]));

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_CredentialUnavailableException_IsExpectedAndTransient()
    {
        var exception = new CredentialUnavailableException("no usable credential");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_HttpRequestException_IsExpectedAndTransient()
    {
        var exception = new HttpRequestException("connection reset");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_MultiInnerAggregateException_IsNotExpected()
    {
        var exception = new AggregateException(
            new RequestFailedException(429, "throttled"),
            new SocketException((int) SocketError.TimedOut));

        var judgement = _sut.GetJudgement(exception);

        Assert.False(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetJudgement(null!));
    }

    [Fact]
    public void GetJudgement_OperationCanceledException_IsExpectedAndNotTransient()
    {
        var exception = new OperationCanceledException("caller cancelled");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public void GetJudgement_RequestFailedException_WithPermanentStatus_IsExpectedAndNotTransient(int status)
    {
        var exception = new RequestFailedException(status, "azure request failed");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void GetJudgement_RequestFailedException_WithTransientStatus_IsExpectedAndTransient(int status)
    {
        var exception = new RequestFailedException(status, "azure request failed");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RequestFailedException(429, "throttled");
        var exception = new AggregateException(inner);

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_SocketException_IsExpectedAndTransient()
    {
        var exception = new SocketException((int) SocketError.TimedOut);

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_TaskCanceledException_IsExpectedAndTransient()
    {
        var exception = new TaskCanceledException("request timed out");

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.True(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_UnrecognizedException_IsNotExpectedAndNotTransient()
    {
        var exception = new InvalidOperationException("unexpected failure");

        var judgement = _sut.GetJudgement(exception);

        Assert.False(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }

    [Fact]
    public void GetJudgement_UriFormatException_IsExpectedAndNotTransient()
    {
        var exception = Assert.Throws<UriFormatException>(() => new Uri("not a uri"));

        var judgement = _sut.GetJudgement(exception);

        Assert.True(judgement.IsExpected);
        Assert.False(judgement.CouldBeTransient);
    }
}