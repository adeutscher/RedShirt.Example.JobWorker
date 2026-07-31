using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services.Redis;

public class RedisLockServiceTests
{
    [Fact]
    public async Task LockerTest_False()
    {
        var db = new Mock<IDatabase>();
        var source = new Mock<IRedisConnectionCacheService>();
        source
            .Setup(s => s.GetDatabaseAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(db.Object);

        var locker = new RedisLockService(source.Object);
        var @lock = await locker.GetLockAsync("abc", TestContext.Current.CancellationToken);
        Assert.NotNull(@lock);
        Assert.False(@lock.IsAcquired);
        @lock.Unlock();
    }
}