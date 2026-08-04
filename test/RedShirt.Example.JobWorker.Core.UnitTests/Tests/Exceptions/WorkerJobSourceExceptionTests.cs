using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Exceptions;

public class WorkerJobSourceExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_PreservesMessageAndInner()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerJobSourceException(inner)
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.False(exception.CouldBeTransient);
        Assert.False(exception.IsHandled);
        Assert.False(exception.CouldBeExternallySolvable);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(
        bool isTransient,
        bool isHandled,
        bool couldBeExternallySolvable)
    {
        var inner = new TimeoutException("timed out talking to job source");

        var exception = new WorkerJobSourceException(inner)
        {
            CouldBeTransient = isTransient,
            IsHandled = isHandled,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isTransient, exception.CouldBeTransient);
        Assert.Equal(isHandled, exception.IsHandled);
        Assert.Equal(couldBeExternallySolvable, exception.CouldBeExternallySolvable);
    }

    [Fact]
    public void Constructor_WithMessage_SetsMessageAndFlags()
    {
        var exception = new WorkerJobSourceException("job source failure")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.Equal("job source failure", exception.Message);
        Assert.False(exception.CouldBeTransient);
        Assert.False(exception.IsHandled);
        Assert.False(exception.CouldBeExternallySolvable);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, "transient handled job source failure")]
    [InlineData(false, false, "permanent unhandled job source failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isTransient,
        bool isHandled,
        string message)
    {
        var exception = new WorkerJobSourceException(message)
            {CouldBeTransient = isTransient, IsHandled = isHandled, CouldBeExternallySolvable = isTransient};

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isTransient, exception.CouldBeTransient);
        Assert.Equal(isHandled, exception.IsHandled);
        Assert.Equal(isTransient, exception.CouldBeExternallySolvable);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerJobSourceException("boom")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.IsAssignableFrom<Exception>(exception);
    }
}