using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Models;

public class JobRepositoryEntryTests
{
    [Fact]
    public async Task TestGettersSetters()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict).Object;
        var rawJobModel = TestJobHelpers.CreateRawJobModel().Object;

        var jre = new JobRepositoryEntry
        {
            LastHeartbeatTime = default,
            JobModel = jobModel,
            RawJobModel = rawJobModel
        };

        Assert.True(jre.CanHeartbeat);
        Assert.Equal(JobState.Inactive, jre.State);

        // Set/Get Heartbeat Time
        Assert.Equal(default, jre.LastHeartbeatTime);
        var newDate = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        jre.LastHeartbeatTime = newDate;
        Assert.Equal(newDate, jre.LastHeartbeatTime);

        // Set/Get State
        await jre.SetStateAsync(JobState.Active, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Active, jre.State);

        // Set/Get FlightTimeCanBeExtended
        await jre.SetIfFlightTimeCanBeExtendedAsync(false, TestContext.Current.CancellationToken);
        Assert.False(jre.CanHeartbeat);
    }

    [Fact(Timeout = 500)]
    public async Task TestLocking()
    {
        var jre = new JobRepositoryEntry
        {
            LastHeartbeatTime = default,
            JobModel = null!,
            RawJobModel = TestJobHelpers.CreateRawJobModel().Object
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
            LastHeartbeatTime = default,
            JobModel = null!,
            RawJobModel = TestJobHelpers.CreateRawJobModel().Object
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
            LastHeartbeatTime = default,
            JobModel = null!,
            RawJobModel = TestJobHelpers.CreateRawJobModel().Object
        };

        var fakeLockId = Guid.NewGuid();

        await Assert.ThrowsAsync<IllegalUnlockException>(() =>
            jre.ReleaseLockAsync(fakeLockId, TestContext.Current.CancellationToken));
    }
}