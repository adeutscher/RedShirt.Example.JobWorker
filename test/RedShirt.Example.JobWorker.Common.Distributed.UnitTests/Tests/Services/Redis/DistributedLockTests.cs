using Medallion.Threading.Redis;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using System.Runtime.CompilerServices;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class DistributedLockTests
{
    /// <summary>
    ///     <see cref="RedisDistributedLockHandle" /> is sealed with an inaccessible constructor.
    ///     An uninitialized instance is enough to exercise the non-null handle branch without talking to Redis;
    ///     tests that would call <c>DisposeAsync</c> stub the retry wrapper so the func is never invoked.
    /// </summary>
    private static RedisDistributedLockHandle CreateOpaqueHandle()
    {
        return (RedisDistributedLockHandle) RuntimeHelpers.GetUninitializedObject(typeof(RedisDistributedLockHandle));
    }

    [Fact]
    public void IsAcquired_WhenHandleIsNull_ReturnsFalse()
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        var @lock = new RedisLockService.DistributedLock(retry.Object, null);

        Assert.False(@lock.IsAcquired);
        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public void IsAcquired_WhenHandleIsPresent_ReturnsTrue()
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        var @lock = new RedisLockService.DistributedLock(retry.Object, CreateOpaqueHandle());

        Assert.True(@lock.IsAcquired);
        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnlockAsync_WhenAlreadyCancelled_ThrowsWithoutCallingRetryWrapper()
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        var @lock = new RedisLockService.DistributedLock(retry.Object, CreateOpaqueHandle());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            @lock.UnlockAsync(cts.Token));

        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnlockAsync_WhenHandleIsNull_DoesNotCallRetryWrapper()
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        var @lock = new RedisLockService.DistributedLock(retry.Object, null);

        await @lock.UnlockAsync(TestContext.Current.CancellationToken);

        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnlockAsync_WhenHandleIsPresent_DisposesThroughRetryWrapper()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Func<CancellationToken, Task>? capturedFunc = null;
        CancellationToken? capturedToken = null;

        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, ct) =>
            {
                capturedFunc = func;
                capturedToken = ct;
                // Do not invoke DisposeAsync on the opaque handle.
                return Task.CompletedTask;
            });

        var @lock = new RedisLockService.DistributedLock(retry.Object, CreateOpaqueHandle());

        await @lock.UnlockAsync(cts.Token);

        Assert.NotNull(capturedFunc);
        Assert.Equal(cts.Token, capturedToken);
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), cts.Token),
            Times.Once);
        retry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnlockAsync_WhenRetryThrowsUnexpectedException_Propagates()
    {
        var expected = new InvalidOperationException("unexpected unlock failure");
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(),
                TestContext.Current.CancellationToken))
            .ThrowsAsync(expected);

        var @lock = new RedisLockService.DistributedLock(retry.Object, CreateOpaqueHandle());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            @lock.UnlockAsync(TestContext.Current.CancellationToken));

        Assert.Same(expected, thrown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnlockAsync_WhenRetryThrowsWorkerDistributedException_Swallows(bool isTransient)
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(),
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerDistributedException("unlock failed", isTransient: isTransient));

        var @lock = new RedisLockService.DistributedLock(retry.Object, CreateOpaqueHandle());

        await @lock.UnlockAsync(TestContext.Current.CancellationToken);

        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(),
                TestContext.Current.CancellationToken),
            Times.Once);
    }
}