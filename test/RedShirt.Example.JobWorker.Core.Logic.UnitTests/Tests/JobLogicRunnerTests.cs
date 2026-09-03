using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Services;

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

        var barConnector = new Mock<IBarConnector>(MockBehavior.Strict);
        barConnector
            .Setup(b => b.GetByIdAsync(1, TestContext.Current.CancellationToken))
            .ReturnsAsync(new GetBarConnectorResponse {Id = 1, Name = "Bar-1"});

        var jobLogicRunner = new JobLogicRunner(
            barConnector.Object,
            sleepService.Object,
            new NullLogger<JobLogicRunner>());

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);
        jobData.Setup(j => j.SleepDurationSeconds).Returns(0);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.Data).Returns(jobData.Object);

        var result = await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(JobResult.Success, result.Result);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken), Times.Once);
        barConnector.Verify(b => b.GetByIdAsync(1, TestContext.Current.CancellationToken), Times.Once);
    }
}