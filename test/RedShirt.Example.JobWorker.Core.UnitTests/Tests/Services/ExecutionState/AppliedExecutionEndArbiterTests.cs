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
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.SubscribeToInactiveCountUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback => callback(inactiveCount));
        jobRepository
            .Setup(r => r.SubscribeToWatchedJobsUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback => callback(watchedCount));
        return jobRepository;
    }

    /// <summary>
    ///     Test with impossible IJobRepository output
    /// </summary>
    [Fact]
    public async Task TestExecutorStopRunningWeird()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(-1).Object,
            CreateSleepService().Object);

        Assert.False(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     All checks return true
    /// </summary>
    [Fact]
    public async Task TestExecutorsKeepRunningA()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsKeepRunningBecauseInactive()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsKeepRunningDespiteWatched()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(0, 1).Object,
            CreateSleepService().Object);

        // Confirming that we're ignoring watched jobs
        Assert.False(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsStopRunning()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            CreateSleepService().Object);

        Assert.False(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     All checks return true
    /// </summary>
    [Fact]
    public async Task TestMaintainerKeepRunningA()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerKeepRunningBecauseInactive()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerKeepRunningBecauseWatched()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(0, 1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            CreateSleepService().Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerStopRunning()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            CreateSleepService().Object);

        Assert.False(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test with impossible IJobRepository output
    /// </summary>
    [Fact]
    public async Task TestMaintainerStopRunningWeird()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(-1, -1).Object,
            CreateSleepService().Object);

        Assert.False(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DelayWithStopAwarenessAsync_CompletesNormallyWhenNeitherTokenCancels()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Keep jobs present so the interrupt signal is not sent.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object);

        await arbiter.DelayWithStopAwarenessAsync(delay, TestContext.Current.CancellationToken);

        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DelayWithStopAwarenessAsync_WhenInterrupted_IgnoresCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        var innerArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        // Empty job counts while still "keep running" cancels the internal interrupt token on subscribe.
        innerArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = CreateSleepService();
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) => Task.FromCanceled(token));

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository().Object,
            sleepService.Object);

        await arbiter.DelayWithStopAwarenessAsync(delay, CancellationToken.None);
    }

    [Fact]
    public async Task DelayWithStopAwarenessAsync_WhenCallerCancels_PropagatesCancellation()
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

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, CreateJobRepository(1, 1).Object,
            sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            arbiter.DelayWithStopAwarenessAsync(delay, callerCts.Token));
    }
}
