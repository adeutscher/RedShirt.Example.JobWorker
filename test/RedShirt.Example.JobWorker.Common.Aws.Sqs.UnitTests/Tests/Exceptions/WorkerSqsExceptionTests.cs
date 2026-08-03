using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.UnitTests.Tests.Exceptions;

public class WorkerSqsExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsToCriticalNotTransientNotHandled()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerSqsException(inner);

        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.False(exception.IsHandled);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void Constructor_WithInnerException_PreservesFlags(bool isCritical, bool isTransient, bool isHandled)
    {
        var inner = new TimeoutException("sqs timeout");

        var exception = new WorkerSqsException(inner, isCritical, isTransient, isHandled);

        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void IsException()
    {
        Assert.IsAssignableFrom<Exception>(new WorkerSqsException("boom"));
    }
}