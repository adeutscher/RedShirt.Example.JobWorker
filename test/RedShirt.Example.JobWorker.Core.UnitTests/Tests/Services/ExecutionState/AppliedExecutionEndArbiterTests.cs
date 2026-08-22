using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using System.Reflection;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class AppliedExecutionEndArbiterTests
{
    private static Mock<ISleepService> CreateSleepService()
    {
        return new Mock<ISleepService>(MockBehavior.Strict);
    }

    private static Mock<IJobRepository> CreateJobRepository(int inactiveCount = 0, int watchedCount = 0)
    {
        return CreateJobRepository(out _, inactiveCount, watchedCount);
    }

    private static Mock<IJobRepository> CreateJobRepository(
        out JobCountNotifier notifier,
        int inactiveCount = 0,
        int watchedCount = 0)
    {
        var captured = new JobCountNotifier();
        notifier = captured;

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.SubscribeToInactiveCountUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback =>
            {
                captured.NotifyInactive = callback;
                callback(inactiveCount);
            });
        jobRepository
            .Setup(r => r.SubscribeToWatchedJobsUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback =>
            {
                captured.NotifyWatched = callback;
                callback(watchedCount);
            });
        return jobRepository;
    }

    [Fact]
    public void CountCallbacks_AfterDispose_AreIgnored()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier, 1, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.MaintainerShouldKeepRunning());
        Assert.True(arbiter.ExecutorsShouldKeepRunning());

        arbiter.Dispose();

        notifier.NotifyInactive(0);
        notifier.NotifyWatched(0);

        // Counts must not change after dispose; keep-running still reflects pre-dispose state.
        Assert.True(arbiter.MaintainerShouldKeepRunning());
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void CountCallbacks_UpdateKeepRunningDecisions()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier, 1, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.ExecutorsShouldKeepRunning());
        Assert.True(arbiter.MaintainerShouldKeepRunning());

        notifier.NotifyInactive(0);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
        Assert.True(arbiter.MaintainerShouldKeepRunning());

        notifier.NotifyWatched(0);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
        Assert.False(arbiter.MaintainerShouldKeepRunning());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        arbiter.Dispose();
        arbiter.Dispose();
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndNoInactive_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(0, 5).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_CompletesNormallyWhenNeitherTokenCancels()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Keep jobs present so the interrupt signal is not sent and the wait event is set.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        await arbiter.MaintainerDelayWaitAsync(delay, "test", "test", TestContext.Current.CancellationToken);

        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenCallerCancelsDuringSleep_PropagatesCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var callerCts = new CancellationTokenSource();

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

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(1, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var delayTask = arbiter.MaintainerDelayWaitAsync(delay, "test", "test", callerCts.Token);
        await delayStarted.Task;

        await callerCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayTask);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenCallerCancels_PropagatesCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Keep jobs present so only the caller token drives cancellation.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) => Task.FromCanceled(token));

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            arbiter.MaintainerDelayWaitAsync(delay, "test", "test", callerCts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenCountsDropToEmptyWhileStopping_InterruptsAndCompletes()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Stopping, but jobs are still present so the interrupt is not sent yet.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier, 1, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var delayTask = arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);
        await delayStarted.Task;

        // Both counts must be empty before the interrupt fires.
        notifier.NotifyInactive(0);
        Assert.False(linkedToken.IsCancellationRequested);

        notifier.NotifyWatched(0);
        Assert.True(linkedToken.IsCancellationRequested);

        await delayTask;
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenDisposed_ReturnsWithoutSleeping()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        arbiter.Dispose();

        await arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);

        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenInterrupted_IgnoresCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Empty job counts while stopping cancels the internal interrupt token on subscribe.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var sleepService = CreateSleepService();

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        await arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);

        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenNoWatchedJobsAndKeepRunning_DoesNotInterrupt()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var delayTask = arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(delayTask.IsCompleted);

        // Empty counts while keep-running must not fire the interrupt; only watched jobs unblock.
        notifier.NotifyInactive(0);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(delayTask.IsCompleted);

        notifier.NotifyWatched(1);
        await delayTask;
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenNoWatchedJobs_WaitsUntilWatchedThenReturnsWithoutSleeping()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var delayTask = arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(delayTask.IsCompleted);

        notifier.NotifyWatched(1);
        await delayTask;

        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenWaitingForWatched_CallerCancelPropagates()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var callerCts = new CancellationTokenSource();

        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository().Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var delayTask = arbiter.MaintainerDelayWaitAsync(delay, "test", "test", callerCts.Token);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(delayTask.IsCompleted);

        await callerCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayTask);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenWaitingForWatched_InterruptCompletesWithoutThrowing()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Stopping with inactive work present: interrupt is deferred until counts clear.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var sleepService = CreateSleepService();

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var delayTask = arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(delayTask.IsCompleted);

        notifier.NotifyInactive(0);
        await delayTask;

        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(Timeout = 5000)]
    public async Task MaintainerDelayWaitAsync_WhenWatchedCountUnchanged_StillTakesSleepPath()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier, 0, 1).Object,
            sleepService.Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        // Same count must not Reset the wait event; sleep path should remain available.
        notifier.NotifyWatched(1);

        await arbiter.MaintainerDelayWaitAsync(delay, "test", "test", CancellationToken.None);

        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void MaintainerShouldKeepRunning_WhenInnerTrueAndNoJobs_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.MaintainerShouldKeepRunning());
    }

    /// <summary>
    ///     Test with impossible IJobRepository output
    /// </summary>
    [Fact]
    public void TestExecutorStopRunningWeird()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(-1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.False(arbiter.ExecutorsShouldKeepRunning());
    }

    /// <summary>
    ///     All checks return true
    /// </summary>
    [Fact]
    public void TestExecutorsKeepRunningA()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void TestExecutorsKeepRunningBecauseInactive()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void TestExecutorsKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void TestExecutorsKeepRunningDespiteWatched()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(0, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        // Confirming that we're ignoring watched jobs
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void TestExecutorsStopRunning()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.False(arbiter.ExecutorsShouldKeepRunning());
    }

    /// <summary>
    ///     All checks return true
    /// </summary>
    [Fact]
    public void TestMaintainerKeepRunningA()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.MaintainerShouldKeepRunning());
    }

    [Fact]
    public void TestMaintainerKeepRunningBecauseInactive()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.MaintainerShouldKeepRunning());
    }

    [Fact]
    public void TestMaintainerKeepRunningBecauseWatched()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(0, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.MaintainerShouldKeepRunning());
    }

    [Fact]
    public void TestMaintainerKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.True(arbiter.MaintainerShouldKeepRunning());
    }

    [Fact]
    public void TestMaintainerStopRunning()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.False(arbiter.MaintainerShouldKeepRunning());
    }

    /// <summary>
    ///     Test with impossible IJobRepository output
    /// </summary>
    [Fact]
    public void TestMaintainerStopRunningWeird()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(-1, -1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        Assert.False(arbiter.MaintainerShouldKeepRunning());
    }

    [Fact]
    public void TryCancelInterrupt_WhenCtsAlreadyDisposed_SwallowsObjectDisposedException()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        using var arbiter = new AppliedExecutionEndArbiter(
            innerArbiter.Object,
            CreateJobRepository(out var notifier, 1).Object,
            CreateSleepService().Object, NullLogger<AppliedExecutionEndArbiter>.Instance);

        var field = typeof(AppliedExecutionEndArbiter).GetField("_interruptCts",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var cts = (CancellationTokenSource) field.GetValue(arbiter)!;
        cts.Dispose();

        // Clearing the last active count would cancel the interrupt CTS; it is already disposed.
        notifier.NotifyInactive(0);

        Assert.False(arbiter.MaintainerShouldKeepRunning());
    }

    private sealed class JobCountNotifier
    {
        public Action<int> NotifyInactive { get; set; } = _ => { };
        public Action<int> NotifyWatched { get; set; } = _ => { };
    }
}