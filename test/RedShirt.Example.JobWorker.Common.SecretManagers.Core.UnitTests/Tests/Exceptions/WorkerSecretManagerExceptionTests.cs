using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.UnitTests.Tests.Exceptions;

public class WorkerSecretManagerExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_DefaultsToCriticalAndNotTransient()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WorkerSecretManagerException(inner);

        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(inner.Message, exception.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Constructor_WithInnerException_PreservesMessageInnerAndFlags(bool isCritical, bool isTransient)
    {
        var inner = new TimeoutException("timed out talking to secrets");

        var exception = new WorkerSecretManagerException(inner, isCritical, isTransient);

        Assert.Equal(inner.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void Constructor_WithMessage_DefaultsToCriticalAndNotTransient()
    {
        var exception = new WorkerSecretManagerException("secret failure");

        Assert.Equal("secret failure", exception.Message);
        Assert.True(exception.IsCritical);
        Assert.False(exception.IsTransient);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true, true, "critical transient secret failure")]
    [InlineData(true, false, "critical permanent secret failure")]
    [InlineData(false, true, "non-critical transient secret failure")]
    [InlineData(false, false, "non-critical permanent secret failure")]
    public void Constructor_WithMessage_SetsMessageAndFlagsWithoutInnerException(
        bool isCritical,
        bool isTransient,
        string message)
    {
        var exception = new WorkerSecretManagerException(message, isCritical, isTransient);

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(isCritical, exception.IsCritical);
        Assert.Equal(isTransient, exception.IsTransient);
    }

    [Fact]
    public void IsException()
    {
        var exception = new WorkerSecretManagerException("boom");

        Assert.IsAssignableFrom<Exception>(exception);
    }
}