using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.UnitTests.Tests.Exceptions;

public class WorkerSecretManagerExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsToCriticalNotTransientNotHandled()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerSecretManagerException(inner);

        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.False(exception.IsHandled);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(
        bool isCritical,
        bool isTransient,
        bool isHandled)
    {
        var inner = new TimeoutException("timed out talking to secrets");

        var exception = new WorkerSecretManagerException(inner, isCritical, isTransient, isHandled);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsToCriticalNotTransientNotHandled()
    {
        var exception = new WorkerSecretManagerException("secret failure");

        Assert.Equal("secret failure", exception.Message);
        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.False(exception.IsHandled);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, false, "critical transient secret failure")]
    [InlineData(true, false, false, "critical permanent secret failure")]
    [InlineData(false, true, true, "non-critical transient handled secret failure")]
    [InlineData(false, false, true, "non-critical permanent handled secret failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isCritical,
        bool isTransient,
        bool isHandled,
        string message)
    {
        var exception = new WorkerSecretManagerException(message, isCritical, isTransient, isHandled);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
        Assert.Equal(isHandled, exception.IsHandled);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerSecretManagerException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}