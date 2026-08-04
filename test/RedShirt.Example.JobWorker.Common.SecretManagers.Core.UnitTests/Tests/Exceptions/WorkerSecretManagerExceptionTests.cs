using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.UnitTests.Tests.Exceptions;

public class WorkerSecretManagerExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_PreservesMessageAndInner()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerSecretManagerException(inner)
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.False(exception.CouldBeTransient);
        Assert.False(exception.IsHandled);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(
        bool isTransient,
        bool isHandled)
    {
        var inner = new TimeoutException("timed out talking to secrets");

        var exception = new WorkerSecretManagerException(inner)
            {CouldBeTransient = isTransient, IsHandled = isHandled, CouldBeExternallySolvable = false};

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isTransient, exception.CouldBeTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void Constructor_WithMessage_SetsMessageAndFlags()
    {
        var exception = new WorkerSecretManagerException("secret failure")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.Equal("secret failure", exception.Message);
        Assert.False(exception.CouldBeTransient);
        Assert.False(exception.IsHandled);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, false, "transient secret failure")]
    [InlineData(false, false, "permanent secret failure")]
    [InlineData(true, true, "transient handled secret failure")]
    [InlineData(false, true, "permanent handled secret failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isTransient,
        bool isHandled,
        string message)
    {
        var exception = new WorkerSecretManagerException(message)
            {CouldBeTransient = isTransient, IsHandled = isHandled, CouldBeExternallySolvable = false};

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isTransient, exception.CouldBeTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerSecretManagerException("boom")
            {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};

        Assert.IsAssignableFrom<Exception>(exception);
    }
}