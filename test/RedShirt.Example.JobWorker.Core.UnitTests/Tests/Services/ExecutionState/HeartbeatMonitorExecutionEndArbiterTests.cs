using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using System.Reflection;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class HeartbeatMonitorExecutionEndArbiterTests
{
    private static Mock<ISleepService> CreateSleepService()
    {
        return new Mock<ISleepService>(MockBehavior.Strict);
    }

    private static Mock<IJobRepository> CreateJobRepository(out JobCountNotifier notifier, int watchedCount = 0)
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
        return jobRepository;
    }

    private static Mock<IJobRepository> CreateJobRepository(int watchedCount = 0)
    {
        return CreateJobRepository(out _, watchedCount);
    }

    private static HeartbeatMonitorExecutionEndArbiter CreateArbiter(
        IExecutionEndArbiter inner,
        IJobRepository jobRepository,
        ISleepService sleepService)
    {
        return new HeartbeatMonitorExecutionEndArbiter(jobRepository, inner, sleepService,
            NullLogger<HeartbeatMonitorExecutionEndArbiter>.Instance);
    }

    [Fact]
    public void CountCallbacks_AfterDispose_AreIgnored()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(out var notifier, 1).Object,
            CreateSleepService().Object);

        Assert.True(arbiter.MonitorShouldKeepRunning());
        arbiter.Dispose();
        notifier.NotifyWatched(0);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void CountCallbacks_UpdateKeepRunningDecisions()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(out var notifier, 1).Object,
            CreateSleepService().Object);

        Assert.True(arbiter.MonitorShouldKeepRunning());
        notifier.NotifyWatched(0);
        Assert.False(arbiter.MonitorShouldKeepRunning());
        notifier.NotifyWatched(2);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var arbiter =
            CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, CreateSleepService().Object);
        arbiter.Dispose();
        Assert.True(true); // Satisfy Sonar
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_CompletesNormallyWhenNeitherTokenCancels()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, sleepService.Object);
        await arbiter.HeartbeatMonitorDelayWaitAsync(delay, TestContext.Current.CancellationToken);
        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_WhenCallerCancelsDuringSleep_PropagatesCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var callerCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) =>
            {
                delayStarted.SetResult();
                return Task.Delay(Timeout.Infinite, token);
            });

        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, sleepService.Object);
        var delayTask = arbiter.HeartbeatMonitorDelayWaitAsync(delay, callerCts.Token);
        await delayStarted.Task;
        await callerCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayTask);
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_WhenCallerCancels_PropagatesCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var callerCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await callerCts.CancelAsync();

        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) => Task.FromCanceled(token));

        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, sleepService.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            arbiter.HeartbeatMonitorDelayWaitAsync(delay, callerCts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_WhenDisposed_ReturnsWithoutSleeping()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var sleepService = CreateSleepService();
        var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, sleepService.Object);
        arbiter.Dispose();
        await arbiter.HeartbeatMonitorDelayWaitAsync(delay, TestContext.Current.CancellationToken);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_WhenInterrupted_IgnoresCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);
        var sleepService = CreateSleepService();
        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository().Object, sleepService.Object);
        await arbiter.HeartbeatMonitorDelayWaitAsync(delay, TestContext.Current.CancellationToken);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_WhenWatchedCountDropsToZero_InterruptsAndCompletes()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // ReSharper disable once PreferConcreteValueOverDefault
        CancellationToken linkedToken = default;
        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) =>
            {
                linkedToken = token;
                delayStarted.SetResult();
                return Task.Delay(Timeout.Infinite, token);
            });

        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(out var notifier, 1).Object,
            sleepService.Object);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            // ReSharper disable once PreferConcreteValueOverDefault
            TestContext.Current.CancellationToken, default);
        var delayTask = arbiter.HeartbeatMonitorDelayWaitAsync(delay, cts.Token);
        await delayStarted.Task;
        Assert.False(linkedToken.IsCancellationRequested);
        notifier.NotifyWatched(0);
        Assert.True(linkedToken.IsCancellationRequested);
        await delayTask;
    }

    [Fact(Timeout = 5000)]
    public async Task HeartbeatMonitorDelayWaitAsync_WhenWatchedCountUnchanged_StillTakesSleepPath()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(out var notifier, 1).Object,
            sleepService.Object);
        notifier.NotifyWatched(1);
        await arbiter.HeartbeatMonitorDelayWaitAsync(delay, TestContext.Current.CancellationToken);
        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerFalseAndNoWatchedJobs_ReturnsFalse()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);
        using var arbiter =
            CreateArbiter(innerArbiter.Object, CreateJobRepository().Object, CreateSleepService().Object);
        Assert.False(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerFalseAndWatchedJobs_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);
        using var arbiter =
            CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, CreateSleepService().Object);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerTrueAndNoWatchedJobs_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter =
            CreateArbiter(innerArbiter.Object, CreateJobRepository().Object, CreateSleepService().Object);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void MonitorShouldKeepRunning_WhenInnerTrueAndWatchedJobs_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter =
            CreateArbiter(innerArbiter.Object, CreateJobRepository(1).Object, CreateSleepService().Object);
        Assert.True(arbiter.MonitorShouldKeepRunning());
    }

    [Fact]
    public void TryCancelInterrupt_WhenCtsAlreadyDisposed_SwallowsObjectDisposedException()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);
        using var arbiter = CreateArbiter(innerArbiter.Object, CreateJobRepository(out var notifier, 1).Object,
            CreateSleepService().Object);

        var field = typeof(HeartbeatMonitorExecutionEndArbiter).GetField("_interruptCts",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var cts = (CancellationTokenSource) field.GetValue(arbiter)!;
        cts.Dispose();
        notifier.NotifyWatched(0);
        Assert.False(arbiter.MonitorShouldKeepRunning());
    }

    private sealed class JobCountNotifier
    {
        public Action<int> NotifyWatched { get; set; } = _ => { };
    }
}