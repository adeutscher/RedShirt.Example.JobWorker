using RedShirt.Example.JobWorker.Common.Azure.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Exceptions;

public class WorkerAzureExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsToCriticalAndNotTransient()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerAzureException(inner);

        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(bool isCritical, bool isTransient)
    {
        var inner = new TimeoutException("timed out talking to azure");

        var exception = new WorkerAzureException(inner, isCritical, isTransient);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsToCriticalAndNotTransient()
    {
        var exception = new WorkerAzureException("azure failure");

        Assert.Equal("azure failure", exception.Message);
        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, "critical transient azure failure")]
    [InlineData(true, false, "critical permanent azure failure")]
    [InlineData(false, true, "non-critical transient azure failure")]
    [InlineData(false, false, "non-critical permanent azure failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isCritical,
        bool isTransient,
        string message)
    {
        var exception = new WorkerAzureException(message, isCritical, isTransient);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerAzureException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}