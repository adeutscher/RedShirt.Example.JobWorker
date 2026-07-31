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
    public void Constructor_WithInnerExceptionOnly_DefaultsIsTransientToTrue()
    {
        var inner = new TimeoutException("timed out");

        var exception = new JobSourceHeartbeatException(inner);

        Assert.True(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
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
    public void IsException()
    {
        var exception = new JobSourceHeartbeatException(new Exception("boom"));

        Assert.IsAssignableFrom<Exception>(exception);
    }
}