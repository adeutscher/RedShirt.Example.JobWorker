using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Exceptions.JobSource;

public class JobSourceAcknowledgementExceptionTests
{
    [Fact]
    public void CanBeCaughtAsException()
    {
        Exception? caught = null;

        try
        {
            throw new JobSourceAcknowledgementException(false, new Exception("permanent failure"));
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        var acknowledgementException = Assert.IsType<JobSourceAcknowledgementException>(caught);
        Assert.False(acknowledgementException.IsTransient);
    }

    [Fact]
    public void CanBeCaughtAsJobWorkerWrapperException()
    {
        JobWorkerWrapperException? caught = null;

        try
        {
            throw new JobSourceAcknowledgementException(true, "transient ack failure");
        }
        catch (JobWorkerWrapperException ex)
        {
            caught = ex;
        }

        var acknowledgementException = Assert.IsType<JobSourceAcknowledgementException>(caught);
        Assert.True(acknowledgementException.IsTransient);
        Assert.Equal("transient ack failure", acknowledgementException.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_WithIsTransientAndInnerException_PreservesBoth(bool isTransient)
    {
        var inner = new InvalidOperationException("ack failed");

        var exception = new JobSourceAcknowledgementException(isTransient, inner);

        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, "could not acknowledge")]
    [InlineData(false, "message expired")]
    public void Constructor_WithIsTransientAndMessage_SetsMessageWithoutInnerException(bool isTransient,
        string message)
    {
        var exception = new JobSourceAcknowledgementException(isTransient, message);

        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void IsDistinctFromJobSourceHeartbeatException()
    {
        Exception exception = new JobSourceAcknowledgementException(true, new Exception("boom"));

        Assert.IsNotType<JobSourceHeartbeatException>(exception);
        Assert.IsType<JobSourceAcknowledgementException>(exception);
    }

    [Fact]
    public void IsJobWorkerWrapperException()
    {
        var exception = new JobSourceAcknowledgementException(false, "boom");

        Assert.IsAssignableFrom<JobWorkerWrapperException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }
}