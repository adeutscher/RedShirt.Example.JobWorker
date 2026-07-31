using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Configuration;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisLockServiceTests
{
    private static Mock<IDistributedRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IDatabase>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IDatabase>>, CancellationToken>((func, ct) => func(ct));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((func, ct) => func(ct));
        return retry;
    }

    [Fact]
    public async Task GetLockAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IDatabase>>>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel {TimeoutSeconds = 1}));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            locker.GetLockAsync("lock-a", cts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task GetLockAsync_WhenLockNotAcquired_ReturnsUnacquiredLock()
    {
        // Loose database mock: DistributedLock.Redis issues Lua scripts we do not emulate here.
        var db = new Mock<IDatabase>();
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        source
            .Setup(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(db.Object);

        var retry = CreatePassthroughRetryWrapper();
        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel
            {
                // Keep the busy-wait short; acquire will not succeed against the mock.
                TimeoutSeconds = 1
            }));

        var @lock = await locker.GetLockAsync("abc", TestContext.Current.CancellationToken);
        Assert.NotNull(@lock);
        Assert.False(@lock.IsAcquired);
        await @lock.UnlockAsync(TestContext.Current.CancellationToken);

        source.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IDatabase>>>(),
                TestContext.Current.CancellationToken), Times.Once);
        // Unacquired lock skips dispose/retry on unlock.
        retry.Verify(
            r => r.RunAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetLockAsync_WhenRetryWrapperThrows_Propagates()
    {
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);
        var expected = new WorkerDistributedException("redis unavailable", true);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IDatabase>>>(),
                TestContext.Current.CancellationToken))
            .ThrowsAsync(expected);

        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel {TimeoutSeconds = 1}));

        var thrown = await Assert.ThrowsAsync<WorkerDistributedException>(() =>
            locker.GetLockAsync("lock-a", TestContext.Current.CancellationToken));

        Assert.Same(expected, thrown);
        source.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void Timeout_ReflectsEffectiveLockConfiguration(int? timeoutSeconds, int expectedEffectiveTimeoutSeconds)
    {
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var retry = new Mock<IDistributedRetryWrapperService>(MockBehavior.Strict);

        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel
            {
                TimeoutSeconds = timeoutSeconds
            }));

        Assert.Equal(TimeSpan.FromSeconds(expectedEffectiveTimeoutSeconds), locker.Timeout);
        source.VerifyNoOtherCalls();
        retry.VerifyNoOtherCalls();
    }

    [Fact(Timeout = 5000)]
    public async Task UnlockAsync_WhenAlreadyCancelled_Throws()
    {
        var db = new Mock<IDatabase>();
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        source
            .Setup(s => s.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(db.Object);

        var retry = CreatePassthroughRetryWrapper();
        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel {TimeoutSeconds = 1}));

        var @lock = await locker.GetLockAsync("abc", TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            @lock.UnlockAsync(cts.Token));
    }
}