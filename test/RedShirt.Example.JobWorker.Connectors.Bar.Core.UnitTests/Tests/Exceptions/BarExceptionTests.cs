using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Core.UnitTests.Tests.Exceptions;

public class BarExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_PreservesMessageAndInner()
    {
        var inner = new InvalidOperationException("underlying failure");

        var exception = new BarException(inner)
        {
            IsHandled = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
        };

        Assert.Equal("underlying failure", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.True(exception.IsHandled);
        Assert.False(exception.CouldBeTransient);
        Assert.True(exception.CouldBeExternallySolvable);
    }

    [Fact]
    public void Constructor_WithMessage_PreservesMessage()
    {
        var exception = new BarException("classified failure");

        Assert.Equal("classified failure", exception.Message);
        Assert.Null(exception.InnerException);
    }
}