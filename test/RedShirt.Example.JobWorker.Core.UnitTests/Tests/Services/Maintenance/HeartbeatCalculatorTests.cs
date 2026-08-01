using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Maintenance;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Maintenance;

public class HeartbeatCalculatorTests
{
    [Fact]
    public void TestIsReadyFalse()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(5); // Recommended heartbeat is every 5 seconds

        var heartbeatCalculator = new HeartbeatCalculator(jobSource.Object);

        var jobEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        // Last ran heartbeat 4 seconds ago
        jobEntry.Setup(j => j.LastHeartbeatTime).Returns(DateTime.UtcNow - TimeSpan.FromSeconds(4));

        Assert.False(heartbeatCalculator.IsReadyForHeartbeat(jobEntry.Object));
    }

    [Fact]
    public void TestIsReadyTrue()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(5); // Recommended heartbeat is every 5 seconds

        var heartbeatCalculator = new HeartbeatCalculator(jobSource.Object);

        var jobEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        // Last ran heartbeat 6 seconds ago
        jobEntry.Setup(j => j.LastHeartbeatTime).Returns(DateTime.UtcNow - TimeSpan.FromSeconds(6));

        Assert.True(heartbeatCalculator.IsReadyForHeartbeat(jobEntry.Object));
    }

    [Fact]
    public void TestTimeToNext5()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(5);

        var heartbeatCalculator = new HeartbeatCalculator(jobSource.Object);

        var jobEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobEntry.Setup(j => j.LastHeartbeatTime).Returns(DateTime.UtcNow - TimeSpan.FromMilliseconds(2500));

        var timeToNext = heartbeatCalculator.TimeUntilNextHeartbeat(jobEntry.Object);
        Assert.InRange(timeToNext, TimeSpan.FromMilliseconds(2250), TimeSpan.FromMilliseconds(2750));
    }

    [Fact]
    public void TestTimeToNextA()
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.RecommendedHeartbeatIntervalSeconds)
            .Returns(5);

        var heartbeatCalculator = new HeartbeatCalculator(jobSource.Object);

        var jobEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobEntry.Setup(j => j.LastHeartbeatTime).Returns(DateTime.UtcNow);

        var timeToNext = heartbeatCalculator.TimeUntilNextHeartbeat(jobEntry.Object);
        Assert.InRange(timeToNext, TimeSpan.FromMilliseconds(4750), TimeSpan.FromMilliseconds(5250));
    }
}