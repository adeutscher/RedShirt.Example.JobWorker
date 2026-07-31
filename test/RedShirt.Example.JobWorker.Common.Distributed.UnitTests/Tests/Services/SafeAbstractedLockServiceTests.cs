using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class SafeAbstractedLockServiceTests
{
    [Theory]
    [InlineData(typeof(CacheConnectionException))]
    [InlineData(typeof(CacheTimeoutException))]
    public async Task GetLockAsync_WhenCacheException_EntersDisgraceAndReturnsPermissiveLock(Type exceptionType)
    {
        var lockName = $"lock-cache-ex-{exceptionType.Name}";
        var inner = new Exception("cache unavailable");
        var exception = (Exception) Activator.CreateInstance(exceptionType, inner)!;

        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        disgraceState.Setup(s => s.IsInDisgracePeriod()).Returns(false);
        disgraceState.Setup(s => s.EnterDisgracePeriod());

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.False(result.IsTrulyAcquired);
        result.Unlock();

        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetLockAsync_WhenInDisgrace_ReturnsPermissiveLockWithoutCallingInnerService()
    {
        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        disgraceState.Setup(s => s.IsInDisgracePeriod()).Returns(true);

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync("lock-a", TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.False(result.IsTrulyAcquired);
        result.Unlock();

        lockService.VerifyNoOtherCalls();
        disgraceState.Verify(s => s.IsInDisgracePeriod(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Fact(Timeout = 15000)]
    public async Task GetLockAsync_WhenInnerAttemptExceedsThresholdButWasAcquired_ReportsTrulyAcquired()
    {
        const string lockName = "lock-slow-acquired";

        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        disgraceState.Setup(s => s.IsInDisgracePeriod()).Returns(false);
        disgraceState.Setup(s => s.EnterDisgracePeriod());

        var innerLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        innerLock.SetupGet(l => l.IsAcquired).Returns(true);
        innerLock.Setup(l => l.Unlock());

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5100), TestContext.Current.CancellationToken);
                return innerLock.Object;
            });

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.True(result.IsTrulyAcquired);

        result.Unlock();
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task GetLockAsync_WhenInnerAttemptExceedsThreshold_EntersDisgraceAndForcesAcquired()
    {
        const string lockName = "lock-slow";

        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        disgraceState.Setup(s => s.IsInDisgracePeriod()).Returns(false);
        disgraceState.Setup(s => s.EnterDisgracePeriod());

        var innerLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        // Even if the underlying lock was not acquired, a slow attempt forces IsAcquired.
        innerLock.SetupGet(l => l.IsAcquired).Returns(false);
        innerLock.Setup(l => l.Unlock());

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5100), TestContext.Current.CancellationToken);
                return innerLock.Object;
            });

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.False(result.IsTrulyAcquired);

        result.Unlock();
        innerLock.Verify(l => l.Unlock(), Times.Once);

        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLockAsync_WhenInnerSucceedsQuickly_WrapsInnerLock(bool isAcquired)
    {
        var lockName = $"lock-quick-{isAcquired}";

        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        disgraceState.Setup(s => s.IsInDisgracePeriod()).Returns(false);

        var innerLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        innerLock.SetupGet(l => l.IsAcquired).Returns(isAcquired);
        innerLock.Setup(l => l.Unlock());

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ReturnsAsync(innerLock.Object);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.Equal(isAcquired, result.IsAcquired);
        Assert.Equal(isAcquired, result.IsTrulyAcquired);

        result.Unlock();
        innerLock.Verify(l => l.Unlock(), Times.Once);

        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Never);
    }

    [Fact]
    public async Task GetLockAsync_WhenNonCacheException_Propagates()
    {
        const string lockName = "lock-unexpected";

        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        disgraceState.Setup(s => s.IsInDisgracePeriod()).Returns(false);

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetLockAsync(lockName, TestContext.Current.CancellationToken));

        disgraceState.Verify(s => s.IsInDisgracePeriod(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Never);
    }
}