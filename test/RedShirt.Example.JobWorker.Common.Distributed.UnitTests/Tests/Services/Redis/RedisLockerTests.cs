using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Configuration;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisLockServiceTests
{
    [Fact(Timeout = 5000)]
    public async Task GetLockAsync_WhenLockNotAcquired_ReturnsUnacquiredLock()
    {
        // Loose database mock: DistributedLock.Redis issues Lua scripts we do not emulate here.
        var db = new Mock<IDatabase>();
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        source
            .Setup(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(db.Object);

        var locker = new RedisLockService(source.Object,
            Options.Create(new LockConfigurationModel
            {
                // Keep the busy-wait short; acquire will not succeed against the mock.
                TimeoutSeconds = 1
            }));

        var @lock = await locker.GetLockAsync("abc", TestContext.Current.CancellationToken);
        Assert.NotNull(@lock);
        Assert.False(@lock.IsAcquired);
        @lock.Unlock();

        source.Verify(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    public void Timeout_ReflectsEffectiveLockConfiguration(int? timeoutSeconds, int expectedEffectiveTimeoutSeconds)
    {
        var source = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);

        var locker = new RedisLockService(source.Object,
            Options.Create(new LockConfigurationModel
            {
                TimeoutSeconds = timeoutSeconds
            }));

        Assert.Equal(TimeSpan.FromSeconds(expectedEffectiveTimeoutSeconds), locker.Timeout);
        source.VerifyNoOtherCalls();
    }
}