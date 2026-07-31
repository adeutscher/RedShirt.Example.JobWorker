using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.UnitTests.Tests.Exceptions;

public class WorkerSecretManagerExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsFlagsToFalse()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerSecretManagerException(inner);

        Assert.False(exception.IsExpected);
        Assert.False(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(bool isExpected, bool isTransient)
    {
        var inner = new TimeoutException("timed out talking to secrets");

        var exception = new WorkerSecretManagerException(inner, isExpected, isTransient);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isExpected, exception.IsExpected);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsFlagsToFalse()
    {
        var exception = new WorkerSecretManagerException("secret failure");

        Assert.Equal("secret failure", exception.Message);
        Assert.False(exception.IsExpected);
        Assert.False(exception.IsTransient);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, "expected transient secret failure")]
    [InlineData(true, false, "expected permanent secret failure")]
    [InlineData(false, true, "unexpected transient secret failure")]
    [InlineData(false, false, "unexpected permanent secret failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isExpected,
        bool isTransient,
        string message)
    {
        var exception = new WorkerSecretManagerException(message, isExpected, isTransient);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isExpected, exception.IsExpected);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerSecretManagerException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}