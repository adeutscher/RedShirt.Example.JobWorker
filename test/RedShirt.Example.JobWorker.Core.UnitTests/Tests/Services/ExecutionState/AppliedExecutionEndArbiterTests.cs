using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
    public async Task DelayMaintainerWithStopAwarenessAsync_CompletesNormallyWhenNeitherTokenCancels()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Keep jobs present so the interrupt signal is not sent.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object);

        await arbiter.DelayMaintainerWithStopAwarenessAsync(delay, TestContext.Current.CancellationToken);

        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DelayMaintainerWithStopAwarenessAsync_WhenCallerCancels_PropagatesCancellation()
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
            sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            arbiter.DelayMaintainerWithStopAwarenessAsync(delay, callerCts.Token));
    }

    [Fact]
    public async Task DelayMaintainerWithStopAwarenessAsync_WhenCountsDropToEmptyWhileStopping_InterruptsAndCompletes()
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
            sleepService.Object);

        var delayTask = arbiter.DelayMaintainerWithStopAwarenessAsync(delay, CancellationToken.None);
        await delayStarted.Task;

        // Both counts must be empty before the interrupt fires.
        notifier.NotifyInactive(0);
        Assert.False(linkedToken.IsCancellationRequested);

        notifier.NotifyWatched(0);
        Assert.True(linkedToken.IsCancellationRequested);

        await delayTask;
    }

    [Fact]
    public async Task DelayMaintainerWithStopAwarenessAsync_WhenDisposed_ReturnsWithoutSleeping()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object);

        arbiter.Dispose();

        await arbiter.DelayMaintainerWithStopAwarenessAsync(delay, CancellationToken.None);

        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DelayMaintainerWithStopAwarenessAsync_WhenEmptyButInnerSaysKeepRunning_DoesNotInterrupt()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) =>
            {
                Assert.False(token.IsCancellationRequested);
                return Task.CompletedTask;
            });

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            sleepService.Object);

        await arbiter.DelayMaintainerWithStopAwarenessAsync(delay, CancellationToken.None);

        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DelayMaintainerWithStopAwarenessAsync_WhenInterrupted_IgnoresCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Empty job counts while stopping cancels the internal interrupt token on subscribe.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) => Task.FromCanceled(token));

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            sleepService.Object);

        await arbiter.DelayMaintainerWithStopAwarenessAsync(delay, CancellationToken.None);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            CreateSleepService().Object);

        arbiter.Dispose();
        arbiter.Dispose();
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndNoInactive_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(0, 5).Object,
            CreateSleepService().Object);

        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void MaintainerShouldKeepRunning_WhenInnerTrueAndNoJobs_ReturnsTrue()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        using var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

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
            CreateSleepService().Object);

        Assert.False(arbiter.MaintainerShouldKeepRunning());
    }

    private sealed class JobCountNotifier
    {
        public Action<int> NotifyInactive { get; set; } = _ => { };
        public Action<int> NotifyWatched { get; set; } = _ => { };
    }
}