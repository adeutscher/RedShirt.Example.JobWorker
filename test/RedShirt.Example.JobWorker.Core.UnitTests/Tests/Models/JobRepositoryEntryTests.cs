using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Models;

public class JobRepositoryEntryTests
{
    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        var jre = new JobRepositoryEntry
        {
            JobModel = new Mock<IJobModel>(MockBehavior.Strict).Object,
            RawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict).Object
        };

        var writers = Enumerable.Range(0, 8).Select(async i =>
        {
            for (var n = 0; n < 100; n++)
            {
                await jre.SetStateAsync((JobState) (n % 4), TestContext.Current.CancellationToken);
                await jre.SetLastHeartbeatTimeAsync(DateTime.UtcNow.AddSeconds(-n),
                    TestContext.Current.CancellationToken);
                if (i == 0 && n == 50)
                {
                    await jre.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken);
                }
            }
        });

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
    public async Task TestGettersSetters()
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
        await jre.SetLastHeartbeatTimeAsync(newDate, TestContext.Current.CancellationToken);
        Assert.Equal(newDate, jre.LastHeartbeatTime);

        // Set/Get State
        await jre.SetStateAsync(JobState.Active, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Active, jre.State);

        // Set/Get FlightTimeCanBeExtended
        await jre.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken);
        Assert.False(jre.CanHeartbeat);
    }
}