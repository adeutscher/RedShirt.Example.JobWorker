using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Exceptions;

public class WorkerJobSourceExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsFlagsToCriticalUnhandledNotTransient()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerJobSourceException(inner);

        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.False(exception.IsHandled);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(
        bool isCritical,
        bool isTransient,
        bool isHandled)
    {
        var inner = new TimeoutException("timed out talking to job source");

        var exception = new WorkerJobSourceException(inner, isCritical, isTransient, isHandled);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsFlagsToCriticalUnhandledNotTransient()
    {
        var exception = new WorkerJobSourceException("job source failure");

        Assert.Equal("job source failure", exception.Message);
        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.False(exception.IsHandled);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, true, "critical transient handled job source failure")]
    [InlineData(true, false, false, "critical permanent unhandled job source failure")]
    [InlineData(false, true, true, "non-critical transient handled job source failure")]
    [InlineData(false, false, false, "non-critical permanent unhandled job source failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isCritical,
        bool isTransient,
        bool isHandled,
        string message)
    {
        var exception = new WorkerJobSourceException(message, isCritical, isTransient, isHandled);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerJobSourceException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}
