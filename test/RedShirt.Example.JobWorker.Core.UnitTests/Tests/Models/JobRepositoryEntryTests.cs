using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Models;

public class JobRepositoryEntryTests
{
    [Fact]
    public async Task TestGettersSetters()
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict).Object;
        var rawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict).Object;

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
        await jre.SetAsCannotHeartbeatAsync(TestContext.Current.CancellationToken);
        Assert.False(jre.CanHeartbeat);
    }
}
