using RedShirt.Example.JobWorker.Common.Azure.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Exceptions;

public class WorkerDistributedExceptionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndTransient(bool isTransient)
    {
        var inner = new TimeoutException("timed out talking to azure");

        var exception = new WorkerAzureException(inner, isTransient);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void Constructor_WithInnerException_DefaultsIsTransientToFalse()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerAzureException(inner);

        Assert.False(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
    }

    [Theory]
    [InlineData(true, "transient azure failure")]
    [InlineData(false, "permanent azure failure")]
    public void Constructor_WithMessage_SetsMessageWithoutInnerException(bool isTransient, string message)
    {
        var exception = new WorkerAzureException(message, isTransient);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsIsTransientToFalse()
    {
        var exception = new WorkerAzureException("azure failure");

        Assert.Equal("azure failure", exception.Message);
        Assert.False(exception.IsTransient);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerAzureException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}
