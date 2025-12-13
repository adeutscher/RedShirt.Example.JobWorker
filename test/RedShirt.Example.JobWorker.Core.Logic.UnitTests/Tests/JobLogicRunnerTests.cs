using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Logic.UnitTests.Tests;

public class JobLogicRunnerTests
{
    [Fact]
    public async Task Test_RunAsync()
    {
        var jobLogicRunner = new JobLogicRunner(new NullLogger<JobLogicRunner>());

        var job = new Mock<IJobDataModel>();
        job.Setup(j => j.SleepDurationSeconds).Returns(0);

        await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);
    }
}