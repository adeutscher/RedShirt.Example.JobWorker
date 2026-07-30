using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Exceptions;

public class CacheConnectionExceptionTests
{
    [Fact]
    public void Constructor_WrapsInnerException()
    {
        var inner = new InvalidOperationException("connection failed");

        var exception = new CacheConnectionException(inner);

        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Fact]
    public void IsCacheException()
    {
        var exception = new CacheConnectionException(new Exception("boom"));

        Assert.IsAssignableFrom<CacheException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void CanBeCaughtAsCacheException()
    {
        CacheException? caught = null;

        try
        {
            throw new CacheConnectionException(new Exception("boom"));
        }
        catch (CacheException ex)
        {
            caught = ex;
        }

        Assert.IsType<CacheConnectionException>(caught);
    }
}
