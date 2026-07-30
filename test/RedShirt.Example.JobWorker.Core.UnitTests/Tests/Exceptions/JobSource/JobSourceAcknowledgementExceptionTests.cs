using RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Exceptions.JobSource;

public class JobSourceAcknowledgementExceptionTests
{
    [Fact]
    public void Constructor_WithInnerExceptionOnly_DefaultsIsTransientToTrue()
    {
        var inner = new TimeoutException("ack timed out");

        var exception = new JobSourceAcknowledgementException(inner);

        Assert.True(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
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

    [Fact]
    public void IsException()
    {
        var exception = new JobSourceAcknowledgementException(new Exception("boom"));

        Assert.IsAssignableFrom<Exception>(exception);
    }

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
    public void IsDistinctFromJobSourceHeartbeatException()
    {
        Exception exception = new JobSourceAcknowledgementException(new Exception("boom"));

        Assert.IsNotType<JobSourceHeartbeatException>(exception);
        Assert.IsType<JobSourceAcknowledgementException>(exception);
    }
}
