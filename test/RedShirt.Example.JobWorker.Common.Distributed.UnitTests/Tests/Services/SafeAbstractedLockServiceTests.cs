using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Enums;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class SafeAbstractedLockServiceTests
{
    private static readonly DateTime NextAttempt = new(2026, 8, 4, 21, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ExceedsLockTimeoutDelay = TimeSpan.FromMilliseconds(300);

    private static Mock<ISafetyDisgraceStateService> CreateDisgraceState(bool inDisgrace)
    {
        var disgraceState = new Mock<ISafetyDisgraceStateService>(MockBehavior.Strict);
        var nextAttempt = NextAttempt;
        disgraceState.Setup(s => s.IsInDisgracePeriod(out nextAttempt)).Returns(inDisgrace);
        disgraceState.Setup(s => s.GetNextAttemptTime()).Returns(NextAttempt);
        return disgraceState;
    }

    private static Mock<IAbstractedLockService> CreateLockService()
    {
        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService.SetupGet(s => s.Timeout).Returns(LockTimeout);
        return lockService;
    }

    [Fact]
    public async Task GetLockAsync_WhenInDisgrace_ReturnsPermissiveLockWithoutCallingInnerService()
    {
        var disgraceState = CreateDisgraceState(true);

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync("lock-a", TestContext.Current.CancellationToken);

        Assert.Equal(SafeDistributedOperationResult.DisgracePeriod, result.Result);
        Assert.Equal(NextAttempt, result.NextAttemptTime);
        Assert.True(result.Lock.IsAcquired);
        await result.Lock.UnlockAsync(TestContext.Current.CancellationToken);

        lockService.VerifyNoOtherCalls();
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetLockAsync_WhenInnerAttemptExceedsThresholdButWasAcquired_ReportsSuccessWithoutDisgrace()
    {
        const string lockName = "lock-slow-acquired";

        var disgraceState = CreateDisgraceState(false);

        var innerLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        innerLock.SetupGet(l => l.IsAcquired).Returns(true);
        innerLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var lockService = CreateLockService();
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .Returns(async () =>
            {
                await Task.Delay(ExceedsLockTimeoutDelay, TestContext.Current.CancellationToken);
                return innerLock.Object;
            });

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.Equal(SafeDistributedOperationResult.Success, result.Result);
        Assert.Equal(NextAttempt, result.NextAttemptTime);
        Assert.True(result.Lock.IsAcquired);

        await result.Lock.UnlockAsync(TestContext.Current.CancellationToken);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Never);
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.GetNextAttemptTime(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetLockAsync_WhenInnerAttemptExceedsThreshold_EntersDisgraceAndForcesAcquired()
    {
        const string lockName = "lock-slow";

        var disgraceState = CreateDisgraceState(false);
        disgraceState.Setup(s => s.EnterDisgracePeriod());

        var innerLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        // Even if the underlying lock was not acquired, a slow attempt forces IsAcquired.
        innerLock.SetupGet(l => l.IsAcquired).Returns(false);
        innerLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var lockService = CreateLockService();
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .Returns(async () =>
            {
                await Task.Delay(ExceedsLockTimeoutDelay, TestContext.Current.CancellationToken);
                return innerLock.Object;
            });

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.Equal(SafeDistributedOperationResult.Failure, result.Result);
        Assert.True(result.Lock.IsAcquired);

        await result.Lock.UnlockAsync(TestContext.Current.CancellationToken);
        innerLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);

        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        lockService.VerifyGet(s => s.Timeout, Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.GetNextAttemptTime(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLockAsync_WhenInnerSucceedsQuickly_WrapsInnerLock(bool isAcquired)
    {
        var lockName = $"lock-quick-{isAcquired}";

        var disgraceState = CreateDisgraceState(false);

        var innerLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        innerLock.SetupGet(l => l.IsAcquired).Returns(isAcquired);
        innerLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var lockService = CreateLockService();
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ReturnsAsync(innerLock.Object);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.Equal(SafeDistributedOperationResult.Success, result.Result);
        Assert.Equal(NextAttempt, result.NextAttemptTime);
        Assert.Equal(isAcquired, result.Lock.IsAcquired);

        await result.Lock.UnlockAsync(TestContext.Current.CancellationToken);
        innerLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);

        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.GetNextAttemptTime(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Never);
    }

    [Fact]
    public async Task GetLockAsync_WhenNonCacheException_Propagates()
    {
        const string lockName = "lock-unexpected";

        var disgraceState = CreateDisgraceState(false);

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetLockAsync(lockName, TestContext.Current.CancellationToken));

        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLockAsync_WhenNonCriticalDistributedException_EntersDisgraceAndReturnsPermissiveLock(
        bool isTransient)
    {
        var lockName = $"lock-cache-ex-transient-{isTransient}";
        var inner = new Exception("cache unavailable");
        var exception = new WorkerDistributedException(inner)
            {CouldBeTransient = isTransient, IsHandled = false, CouldBeExternallySolvable = isTransient};

        var disgraceState = CreateDisgraceState(false);
        disgraceState.Setup(s => s.EnterDisgracePeriod());

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());
        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.Equal(SafeDistributedOperationResult.Failure, result.Result);
        Assert.Equal(NextAttempt, result.NextAttemptTime);
        Assert.True(result.Lock.IsAcquired);
        await result.Lock.UnlockAsync(TestContext.Current.CancellationToken);

        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.GetNextAttemptTime(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetLockAsync_WhenOperationCanceledAndTokenCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        const string lockName = "lock-cancelled";

        var disgraceState = CreateDisgraceState(false);

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetLockAsync(lockName, cts.Token));

        lockService.Verify(s => s.GetLockAsync(lockName, cts.Token), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Never);
        disgraceState.Verify(s => s.GetNextAttemptTime(), Times.Never);
        disgraceState.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task
        GetLockAsync_WhenUnhandledNonTransientDistributedException_EntersDisgraceAndReturnsPermissiveLock()
    {
        const string lockName = "lock-critical";
        var exception = new WorkerDistributedException(new Exception("auth failed"))
        {
            CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false
        };

        var disgraceState = CreateDisgraceState(false);
        disgraceState.Setup(s => s.EnterDisgracePeriod());

        var lockService = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var service = new SafeAbstractedLockService(disgraceState.Object, lockService.Object,
            new NullLogger<SafeAbstractedLockService>());

        var result = await service.GetLockAsync(lockName, TestContext.Current.CancellationToken);

        Assert.Equal(SafeDistributedOperationResult.Failure, result.Result);
        Assert.True(result.Lock.IsAcquired);
        lockService.Verify(s => s.GetLockAsync(lockName, TestContext.Current.CancellationToken), Times.Once);
        disgraceState.Verify(s => s.IsInDisgracePeriod(out It.Ref<DateTime>.IsAny), Times.Once);
        disgraceState.Verify(s => s.GetNextAttemptTime(), Times.Once);
        disgraceState.Verify(s => s.EnterDisgracePeriod(), Times.Once);
        disgraceState.VerifyNoOtherCalls();
    }
}