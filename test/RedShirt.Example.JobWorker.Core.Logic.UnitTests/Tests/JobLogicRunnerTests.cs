using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services;

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

        var result = await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(JobResult.Success, result);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken), Times.Once);
    }
}