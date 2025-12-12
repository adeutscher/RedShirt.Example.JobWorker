using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class RedisLockerTests
{
    [Fact]
    public async Task LockerTest_False()
    {
        var db = new Mock<IDatabase>();
        var source = new Mock<IRedisConnectionSource>();
        source.Setup(s => s.GetDatabase()).Returns(db.Object);

        var locker = new RedisLocker(source.Object);
        var @lock = await locker.GetLockAsync("abc", TestContext.Current.CancellationToken);
        Assert.NotNull(@lock);
        Assert.False(@lock.IsAcquired);
        @lock.Unlock();
    }
}