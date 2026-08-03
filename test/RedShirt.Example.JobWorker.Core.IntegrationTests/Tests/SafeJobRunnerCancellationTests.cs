using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.IntegrationTests.Tests;

/// <summary>
///     Integration coverage for per-job cancellation via <see cref="TimeBorderWrapperService" />
///     inside <see cref="SafeJobRunner" />.
/// </summary>
public class SafeJobRunnerCancellationTests
{
    /// <summary>
    ///     When job logic cancels a token linked to the job token and throws via
    ///     <see cref="CancellationToken.ThrowIfCancellationRequested" />,
    ///     <see cref="SafeJobRunner" /> reports <see cref="CoreJobResult.Cancelled" />
    ///     without cancelling the caller token.
    /// </summary>
    [Fact(Timeout = 2000)]
    public async Task RunSafelyAsync_WhenJobCancelsItsToken_ReturnsCancelled()
    {
        const int maximumRunTimeSeconds = 30;

        // Job model
        var job = new Mock<IJobModel>(MockBehavior.Strict);

        // Application logic: cancel a linked token immediately, then throw if cancellation requested
        IJobModel? receivedJob = null;
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns((IJobModel data, CancellationToken token) =>
            {
                receivedJob = data;
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                linkedCts.Cancel();
                linkedCts.Token.ThrowIfCancellationRequested();
                return Task.FromResult(JobResult.Success);
            });

        // System under test
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        var timeBorderWrapperService = new TimeBorderWrapperService(
            new SleepService(),
            Options.Create(new TimeBorderWrapperService.ConfigurationModel
            {
                TaskWaitBufferSeconds = null,
                TruantAlertIntervalSeconds = 1
            }),
            NullLogger<TimeBorderWrapperService>.Instance);
        var safeJobRunner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            timeBorderWrapperService,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = maximumRunTimeSeconds
            }));

        using var callerCts = new CancellationTokenSource();
        var originalCancellationToken = callerCts.Token;
        var result = await safeJobRunner.RunSafelyAsync(job.Object, originalCancellationToken);

        Assert.Equal(CoreJobResult.Cancelled, result.Result);
        Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
        Assert.Same(job.Object, receivedJob);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
        // Per-job cancellation must not bleed into the caller's token.
        Assert.False(originalCancellationToken.IsCancellationRequested);
        Assert.False(callerCts.IsCancellationRequested);
    }

    /// <summary>
    ///     When job logic runs longer than <see cref="SafeJobRunner.ConfigurationModel.MaxJobTimeSeconds" />,
    ///     the time border cancels the job token and <see cref="SafeJobRunner" /> reports
    ///     <see cref="CoreJobResult.Cancelled" /> without cancelling the caller token.
    /// </summary>
    [Theory(Timeout = 15000)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RunSafelyAsync_WhenJobExceedsMaxJobTime_ReturnsCancelled(int maximumRunTimeSeconds)
    {
        // Job model
        var job = new Mock<IJobModel>(MockBehavior.Strict);

        // Application logic: intentionally overrun the configured maximum
        IJobModel? receivedJob = null;
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(It.IsAny<IJobModel>(), It.IsAny<CancellationToken>()))
            .Returns(async (IJobModel data, CancellationToken token) =>
            {
                receivedJob = data;
                await Task.Delay(TimeSpan.FromSeconds(maximumRunTimeSeconds + 2), token);
                return JobResult.Success;
            });

        // System under test
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        var timeBorderWrapperService = new TimeBorderWrapperService(
            new SleepService(),
            Options.Create(new TimeBorderWrapperService.ConfigurationModel
            {
                TaskWaitBufferSeconds = null,
                TruantAlertIntervalSeconds = 1
            }),
            NullLogger<TimeBorderWrapperService>.Instance);
        var safeJobRunner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            timeBorderWrapperService,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = maximumRunTimeSeconds
            }));

        using var callerCts = new CancellationTokenSource();
        var originalCancellationToken = callerCts.Token;
        var result = await safeJobRunner.RunSafelyAsync(job.Object, originalCancellationToken);

        Assert.Equal(CoreJobResult.Cancelled, result.Result);
        Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
        Assert.Same(job.Object, receivedJob);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
        // Per-job cancellation must not bleed into the caller's token.
        Assert.False(originalCancellationToken.IsCancellationRequested);
        Assert.False(callerCts.IsCancellationRequested);
    }
}