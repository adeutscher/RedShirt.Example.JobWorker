using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Logic.UnitTests.Tests;

public class JobLogicRunnerTests
{
    [Fact]
    public async Task Test_RunAsync()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobLogicRunner = new JobLogicRunner(sleepService.Object, new NullLogger<JobLogicRunner>());

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);
        jobData.Setup(j => j.SleepDurationSeconds).Returns(0);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.Data).Returns(jobData.Object);

        await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);

        sleepService.Verify(s => s.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken), Times.Once);
    }
}