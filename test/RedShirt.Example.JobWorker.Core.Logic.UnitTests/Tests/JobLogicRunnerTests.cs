using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Logic.UnitTests.Tests;

public class JobLogicRunnerTests
{
    private static IOptions<JobLogicRunner.ConfigurationModel> CreateOptions(string? accessBarEnabled = null)
    {
        return Options.Create(new JobLogicRunner.ConfigurationModel {AccessBarEnabled = accessBarEnabled});
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("not-a-value", false)]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    public void ConfigurationModel_EffectiveAccessBarEnabled(string? accessBarEnabled, bool expected)
    {
        var configuration = new JobLogicRunner.ConfigurationModel
        {
            AccessBarEnabled = accessBarEnabled
        };

        Assert.Equal(expected, configuration.EffectiveAccessBarEnabled);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    public async Task RunAsync_WhenAccessBarDisabled_SleepsFullDurationForBarTestIds(int sleepDurationSeconds)
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(sleepDurationSeconds), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var barConnector = new Mock<IBarConnector>(MockBehavior.Strict);

        var jobLogicRunner = new JobLogicRunner(
            barConnector.Object,
            sleepService.Object,
            CreateOptions(),
            new NullLogger<JobLogicRunner>());

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);
        jobData.Setup(j => j.SleepDurationSeconds).Returns(sleepDurationSeconds);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.Data).Returns(jobData.Object);

        var result = await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(JobResult.Success, result.Result);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(sleepDurationSeconds), TestContext.Current.CancellationToken),
            Times.Once);
        barConnector.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenAccessBarDisabled_SleepsRequestedDurationAndSkipsBarConnector()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var barConnector = new Mock<IBarConnector>(MockBehavior.Strict);

        var jobLogicRunner = new JobLogicRunner(
            barConnector.Object,
            sleepService.Object,
            CreateOptions(),
            new NullLogger<JobLogicRunner>());

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);
        jobData.Setup(j => j.SleepDurationSeconds).Returns(0);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.Data).Returns(jobData.Object);

        var result = await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(JobResult.Success, result.Result);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken), Times.Once);
        barConnector.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    public async Task RunAsync_WhenAccessBarEnabledAndSleepDurationIsBarTestId_SleepsOneSecondAndCallsBarWithThatId(
        int barTestId)
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var barConnector = new Mock<IBarConnector>(MockBehavior.Strict);
        barConnector
            .Setup(b => b.GetByIdAsync(barTestId, TestContext.Current.CancellationToken))
            .ReturnsAsync(new GetBarConnectorResponse {Id = barTestId, Name = $"Bar-{barTestId}"});

        var jobLogicRunner = new JobLogicRunner(
            barConnector.Object,
            sleepService.Object,
            CreateOptions("true"),
            new NullLogger<JobLogicRunner>());

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);
        jobData.Setup(j => j.SleepDurationSeconds).Returns(barTestId);

        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.Data).Returns(jobData.Object);

        var result = await jobLogicRunner.RunAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(JobResult.Success, result.Result);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken),
            Times.Once);
        barConnector.Verify(b => b.GetByIdAsync(barTestId, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenAccessBarEnabledAndSleepDurationIsZero_SleepsZeroSecondsAndCallsBarWithIdOne()
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
            CreateOptions("true"),
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