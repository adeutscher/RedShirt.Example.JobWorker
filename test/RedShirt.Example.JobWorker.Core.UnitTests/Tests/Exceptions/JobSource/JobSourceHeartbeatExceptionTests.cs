using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Exceptions.JobSource;

public class JobSourceHeartbeatExceptionTests
{
    [Fact]
    public void CanBeCaughtAsException()
    {
        Exception? caught = null;

        try
        {
            throw new JobSourceHeartbeatException(false, "permanent failure");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        var heartbeatException = Assert.IsType<JobSourceHeartbeatException>(caught);
        Assert.False(heartbeatException.IsTransient);
    }

    [Fact]
    public void CanBeCaughtAsJobWorkerWrapperException()
    {
        JobWorkerWrapperException? caught = null;

        try
        {
            throw new JobSourceHeartbeatException(true, new TimeoutException("heartbeat timed out"));
        }
        catch (JobWorkerWrapperException ex)
        {
            caught = ex;
        }

        var heartbeatException = Assert.IsType<JobSourceHeartbeatException>(caught);
        Assert.True(heartbeatException.IsTransient);
        Assert.Equal("heartbeat timed out", heartbeatException.Message);
        Assert.IsType<TimeoutException>(heartbeatException.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_WithIsTransientAndInnerException_PreservesBoth(bool isTransient)
    {
        var inner = new InvalidOperationException("heartbeat failed");

        var exception = new JobSourceHeartbeatException(isTransient, inner);

        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, "could not renew lock")]
    [InlineData(false, "message expired")]
    public void Constructor_WithIsTransientAndMessage_SetsMessageWithoutInnerException(bool isTransient,
        string message)
    {
        var exception = new JobSourceHeartbeatException(isTransient, message);

        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void IsDistinctFromJobSourceAcknowledgementException()
    {
        Exception exception = new JobSourceHeartbeatException(true, new Exception("boom"));

        Assert.IsNotType<JobSourceAcknowledgementException>(exception);
        Assert.IsType<JobSourceHeartbeatException>(exception);
    }

    [Fact]
    public void IsJobWorkerWrapperException()
    {
        var exception = new JobSourceHeartbeatException(false, "boom");

        Assert.IsAssignableFrom<JobWorkerWrapperException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }
}