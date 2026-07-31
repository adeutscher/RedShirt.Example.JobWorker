using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Exceptions.JobSource;

public class WorkerJobSourceExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsToCriticalAndNotTransient()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerJobSourceException(inner);

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
        var inner = new TimeoutException("timed out talking to job source");

        var exception = new WorkerJobSourceException(inner, isCritical, isTransient);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsToCriticalAndNotTransient()
    {
        var exception = new WorkerJobSourceException("job source failure");

        Assert.Equal("job source failure", exception.Message);
        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, "critical transient job source failure")]
    [InlineData(true, false, "critical permanent job source failure")]
    [InlineData(false, true, "non-critical transient job source failure")]
    [InlineData(false, false, "non-critical permanent job source failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isCritical,
        bool isTransient,
        string message)
    {
        var exception = new WorkerJobSourceException(message, isCritical, isTransient);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerJobSourceException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}
