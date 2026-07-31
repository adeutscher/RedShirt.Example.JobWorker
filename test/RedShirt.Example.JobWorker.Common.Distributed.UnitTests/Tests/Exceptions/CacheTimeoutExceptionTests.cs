using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Exceptions;

public class CacheTimeoutExceptionTests
{
    [Fact]
    public void CanBeCaughtAsCacheException()
    {
        CacheException? caught = null;

        try
        {
            throw new CacheTimeoutException(new Exception("boom"));
        }
        catch (CacheException ex)
        {
            caught = ex;
        }

        Assert.IsType<CacheTimeoutException>(caught);
    }

    [Fact]
    public void Constructor_WrapsInnerException()
    {
        var inner = new TimeoutException("timed out");

        var exception = new CacheTimeoutException(inner);

        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Fact]
    public void IsCacheException()
    {
        var exception = new CacheTimeoutException(new Exception("boom"));

        Assert.IsAssignableFrom<CacheException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }
}