using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class IdempotencyMonitorExecutionEndArbiterTests
{
    private static Mock<IJobRepository> CreateJobRepository(out JobCountNotifier notifier, int watchedCount = 0,
        int blockedCount = 0)
    {
        var captured = new JobCountNotifier();
        notifier = captured;
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.SubscribeToWatchedJobsUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback =>
            {
                captured.NotifyWatched = callback;
                callback(watchedCount);
            });
        jobRepository
            .Setup(r => r.SubscribeToIdempotencyBlockedCountUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback =>
            {
                captured.NotifyBlocked = callback;
                callback(blockedCount);
            });
        return jobRepository;
    }

    private static IdempotencyMonitorExecutionEndArbiter CreateArbiter(IExecutionEndArbiter inner,
        IJobRepository jobRepository, ISleepService? sleepService = null)
    {
        return new IdempotencyMonitorExecutionEndArbiter(jobRepository, inner,
            sleepService ?? new Mock<ISleepService>(MockBehavior.Strict).Object,
            NullLogger<IdempotencyMonitorExecutionEndArbiter>.Instance);
    }

    [Fact]
    public void CountCallbacks_AfterDispose_AreIgnored()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out var notifier, 1, 1).Object);
        Assert.True(arbiter.MonitorShouldKeepRunning());
        arbiter.Dispose();
        notifier.NotifyWatched(0);
        notifier.NotifyBlocked(0);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1).Object);
        arbiter.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task IdempotencyMonitorDelayWaitAsync_CompletesNormallyWhenWatchedJobsExist()
    {
        var delay = TimeSpan.FromSeconds(5);
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1, 1).Object, sleepService.Object);
        await arbiter.IdempotencyMonitorDelayWaitAsync(delay, CancellationToken.None);
        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 5000)]
    public async Task IdempotencyMonitorDelayWaitAsync_WhenDisposed_ReturnsWithoutSleeping()
    {
        var delay = TimeSpan.FromSeconds(5);
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1).Object, sleepService.Object);
        arbiter.Dispose();
        await arbiter.IdempotencyMonitorDelayWaitAsync(delay, CancellationToken.None);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerFalseAndNoWatchedJobs_ReturnsFalse()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(false);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _).Object);
        Assert.False(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerFalseAndWatchedJobs_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(false);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out var notifier, 1).Object);
        Assert.True(arbiter.MonitorShouldKeepRunning());
        notifier.NotifyWatched(0);
        Assert.False(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerTrueAndNoWatchedJobs_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _).Object);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    private sealed class JobCountNotifier
    {
        public Action<int> NotifyBlocked { get; set; } = _ => { };
        public Action<int> NotifyWatched { get; set; } = _ => { };
    }
}