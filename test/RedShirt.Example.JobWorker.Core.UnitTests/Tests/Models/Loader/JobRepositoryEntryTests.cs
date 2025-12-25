using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Models.Loader;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Models.Loader;

public class JobRepositoryEntryTests
{
    [Fact]
    public void TestGettersSetters()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict).Object;

        var jre = new JobRepositoryEntry
        {
            FlightTimeCanBeExtended = true,
            LastHeartbeatTime = default,
            JobModel = jobModel,
            State = JobState.Inactive
        };

        // Set/Get Heartbeat Time
        Assert.Equal(default, jre.LastHeartbeatTime);
        var newDate = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        jre.LastHeartbeatTime = newDate;
        Assert.Equal(newDate, jre.LastHeartbeatTime);

        // Set/Get State
        jre.State = JobState.Active;
        Assert.Equal(JobState.Active, jre.State);

        // Set/Get FlightTimeCanBeEx
        jre.FlightTimeCanBeExtended = false;
        Assert.False(jre.FlightTimeCanBeExtended);
    }

    [Fact(Timeout = 500)]
    public async Task TestLocking()
    {
        var jre = new JobRepositoryEntry
        {
            FlightTimeCanBeExtended = true,
            LastHeartbeatTime = default,
            JobModel = null!,
            State = JobState.Inactive
        };

        var lockId = await jre.AcquireLockAsync(TestContext.Current.CancellationToken);

        var secondAcquire = jre.AcquireLockAsync(TestContext.Current.CancellationToken);

        // Should be still waiting after a delay because of the semaphore in the implementation.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(secondAcquire.IsCompleted);

        await jre.ReleaseLockAsync(lockId, TestContext.Current.CancellationToken);
        var lockId2 = await secondAcquire;
        Assert.NotEqual(lockId, lockId2);
    }

    [Fact(Timeout = 500)]
    public async Task TestLockingIllegalUnlockA()
    {
        var jre = new JobRepositoryEntry
        {
            FlightTimeCanBeExtended = true,
            LastHeartbeatTime = default,
            JobModel = null!,
            State = JobState.Inactive
        };

        await jre.AcquireLockAsync(TestContext.Current.CancellationToken); // Don't care about storing this lock id.
        var fakeLockId = Guid.NewGuid();

        await Assert.ThrowsAsync<IllegalUnlockException>(() =>
            jre.ReleaseLockAsync(fakeLockId, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 500)]
    public async Task TestLockingIllegalUnlockB()
    {
        var jre = new JobRepositoryEntry
        {
            FlightTimeCanBeExtended = true,
            LastHeartbeatTime = default,
            JobModel = null!,
            State = JobState.Inactive
        };

        var fakeLockId = Guid.NewGuid();

        await Assert.ThrowsAsync<IllegalUnlockException>(() =>
            jre.ReleaseLockAsync(fakeLockId, TestContext.Current.CancellationToken));
    }
}