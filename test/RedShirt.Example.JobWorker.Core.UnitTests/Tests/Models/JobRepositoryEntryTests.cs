using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Models;

public class JobRepositoryEntryTests
{
    private static JobRepositoryEntry CreateEntry()
    {
        return new JobRepositoryEntry
        {
            JobModel = new Mock<IJobModel>(MockBehavior.Strict).Object,
            RawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict).Object
        };
    }

    [Fact]
    public void CanHeartbeat_WhenSetTrue_ThrowsArgumentException()
    {
        var jre = CreateEntry();

        var ex = Assert.Throws<ArgumentException>(() => jre.CanHeartbeat = true);
        Assert.Equal("value", ex.ParamName);
        Assert.True(jre.CanHeartbeat);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        var jre = CreateEntry();

        var writers = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            for (var n = 0; n < 100; n++)
            {
                jre.State = (JobState) (n % 4);
                jre.LastHeartbeatTime = DateTime.UtcNow.AddSeconds(-n);
                if (i == 0 && n == 50)
                {
                    jre.CanHeartbeat = false;
                }
            }
        }, TestContext.Current.CancellationToken));

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var n = 0; n < 100; n++)
            {
                GC.KeepAlive(jre.State);
                GC.KeepAlive(jre.CanHeartbeat);
                GC.KeepAlive(jre.LastHeartbeatTime);
            }
        }, TestContext.Current.CancellationToken));

        await Task.WhenAll(writers.Concat(readers));
        Assert.False(jre.CanHeartbeat);
    }

    [Fact]
    public void SubscribeToStateChange_WhenNull_ThrowsArgumentNullException()
    {
        var jre = CreateEntry();

        Assert.Throws<ArgumentNullException>(() => jre.SubscribeToStateChange(null!));
    }

    [Fact]
    public void SubscribeToStateChange_WhenStateChanges_InvokesCallbacks()
    {
        var jre = CreateEntry();
        var first = new List<JobState>();
        var second = new List<JobState>();

        jre.SubscribeToStateChange(first.Add);
        jre.SubscribeToStateChange(second.Add);

        jre.State = JobState.Active;
        jre.State = JobState.Active;
        jre.State = JobState.Complete;

        Assert.Equal([JobState.Active, JobState.Complete], first);
        Assert.Equal([JobState.Active, JobState.Complete], second);
        Assert.Equal(JobState.Complete, jre.State);
    }

    [Fact]
    public void TestGettersSetters()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict).Object;
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict).Object;

        var jre = new JobRepositoryEntry
        {
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
        jre.State = JobState.Active;
        Assert.Equal(JobState.Active, jre.State);

        // Set/Get CanHeartbeat (false only)
        jre.CanHeartbeat = false;
        Assert.False(jre.CanHeartbeat);
    }
}