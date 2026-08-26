using Azure;
using Azure.Core;
using Azure.Identity;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Services.Resilience;

public class AzureExceptionArbiterServiceTests
{
    private readonly AzureExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
#pragma warning disable S3928
        // ReSharper disable once NotResolvedInText
        var exception = new ArgumentException("secret name is empty", "name");
#pragma warning restore S3928
        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_ArgumentNullException_IsExpectedAndNotTransient()
    {
#pragma warning disable S3928
        // ReSharper disable once NotResolvedInText
        var exception = new ArgumentNullException("name");
#pragma warning disable S3928

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_AuthenticationFailedException_IsExpectedAndTransient()
    {
        var exception = new AuthenticationFailedException("token acquisition failed");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_AuthenticationRequiredException_IsExpectedAndNotTransient()
    {
        var exception = new AuthenticationRequiredException(
            "interactive auth required",
            new TokenRequestContext(["https://vault.azure.net/.default"]));

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_CredentialUnavailableException_IsExpectedAndTransient()
    {
        var exception = new CredentialUnavailableException("no usable credential");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_HttpRequestException_IsExpectedAndTransient()
    {
        var exception = new HttpRequestException("connection reset");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotExpected()
    {
        var exception = new AggregateException(
            new RequestFailedException(429, "throttled"),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

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
    public void GetReport_OperationCanceledException_IsExpectedAndNotTransient()
    {
        var exception = new OperationCanceledException("caller cancelled");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(404, true)]
    [InlineData(409, false)]
    public void GetReport_RequestFailedException_WithPermanentStatus_IsExpectedAndNotTransient(
        int status,
        bool couldBeExternallySolvable)
    {
        var exception = new RequestFailedException(status, "azure request failed");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void GetReport_RequestFailedException_WithTransientStatus_IsExpectedAndTransient(int status)
    {
        var exception = new RequestFailedException(status, "azure request failed");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new RequestFailedException(429, "throttled");
        var exception = new AggregateException(inner);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SocketException_IsExpectedAndTransient()
    {
        var exception = new SocketException((int) SocketError.TimedOut);

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsExpectedAndTransient()
    {
        var exception = new TaskCanceledException("request timed out");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TimeoutException_IsExpectedAndTransient()
    {
        var exception = new TimeoutException("operation timed out");

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsNotExpectedAndNotTransient()
    {
        var exception = new InvalidOperationException("unexpected failure");

        var report = _sut.GetReport(exception);

        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_UriFormatException_IsExpectedAndNotTransient()
    {
        var exception = Assert.Throws<UriFormatException>(() => new Uri("not a uri"));

        var report = _sut.GetReport(exception);

        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }
}