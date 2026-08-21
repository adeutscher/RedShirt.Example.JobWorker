using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Configuration;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis.Resilience;
using StackExchange.Redis;
using System.Diagnostics;

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

    /// <summary>
    ///     DistributedLock.Redis acquire uses
    ///     <see cref="IDatabase.StringSetAsync(RedisKey, RedisValue, TimeSpan?, When, CommandFlags)" />.
    ///     Leaving that call incomplete simulates a hung Redis command.
    /// </summary>
    private static Mock<IDatabase> CreateHungAcquireDatabase()
    {
        var db = new Mock<IDatabase>();
        var never = new TaskCompletionSource<bool>().Task;
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Returns(never);
        return db;
    }

    [Fact(Timeout = 5000)]
    public async Task GetLockAsync_WhenCancelledDuringAcquire_PropagatesOperationCanceledException()
    {
        var db = CreateHungAcquireDatabase();
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        source
            .Setup(s => s.GetDatabaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(db.Object);

        var retry = CreatePassthroughRetryWrapper();
        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel {TimeoutSeconds = 10}));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var acquireTask = locker.GetLockAsync("lock-cancel-during", cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquireTask);
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
                // Contention fails immediately; TimeoutSeconds is only network-error leeway.
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
        var expected = new WorkerDistributedException("redis unavailable")
            {CouldBeTransient = true, IsHandled = false, CouldBeExternallySolvable = true};
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

    [Fact(Timeout = 5000)]
    public async Task GetLockAsync_WhenTimeoutElapses_ReturnsUnacquiredLock()
    {
        var db = CreateHungAcquireDatabase();
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        source
            .Setup(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(db.Object);

        var retry = CreatePassthroughRetryWrapper();
        var locker = new RedisLockService(retry.Object, source.Object,
            Options.Create(new LockConfigurationModel {TimeoutSeconds = 1}));

        var stopwatch = Stopwatch.StartNew();
        var @lock = await locker.GetLockAsync("lock-timeout", TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.NotNull(@lock);
        Assert.False(@lock.IsAcquired);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(2500));
        await @lock.UnlockAsync(TestContext.Current.CancellationToken);
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