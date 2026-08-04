using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.UnitTests.Tests.Exceptions;

public class WorkerSqsExceptionTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void Constructor_WithInnerException_PreservesFlags(
        bool isTransient, bool isHandled, bool couldBeExternallySolvable)
    {
        var inner = new TimeoutException("sqs timeout");

        var exception = new WorkerSqsException(inner)
        {
            CouldBeTransient = isTransient,
            IsHandled = isHandled,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };

        Assert.Equal(isTransient, exception.CouldBeTransient);
        Assert.Equal(isHandled, exception.IsHandled);
        Assert.Equal(couldBeExternallySolvable, exception.CouldBeExternallySolvable);
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesMessageAndInner()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerSqsException(inner)
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.False(exception.CouldBeTransient);
        Assert.False(exception.IsHandled);
        Assert.False(exception.CouldBeExternallySolvable);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Fact]
    public void IsException()
    {
        Assert.IsAssignableFrom<Exception>(new WorkerSqsException("boom")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false});
    }
}