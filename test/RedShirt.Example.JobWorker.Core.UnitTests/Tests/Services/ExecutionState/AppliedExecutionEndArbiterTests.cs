using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class AppliedExecutionEndArbiterTests
{
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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(-1); // Implementation should never return negatives, but here we are

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsKeepRunningBecauseInactive()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // Job repository says there is still an in-flight inactive job to be run
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // No watched jobs

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // Job repository says there is still an in-flight job to be run

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestExecutorsKeepRunningDespiteWatched()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // No inactive jobs
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // There is a watched job

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // Job repository says there are no inactive items

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // Job repository says that there are currently-watched jobs

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerKeepRunningBecauseInactive()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // Job repository says there is still an in-flight inactive job to be run
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // No watched jobs

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerKeepRunningBecauseWatched()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // No inactive jobs
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // There is a watched job

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // Job repository says there is still an in-flight job to be run
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestMaintainerStopRunning()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // Job repository says there are no inactive items
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // Job repository says there are no inactive items

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

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

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(-1); // Implementation should never return negatives, but here we are
        jobRepository
            .Setup(r => r.GetWatchedJobsCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(-1); // Implementation should never return negatives, but here we are

        var arbiter = new AppliedExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.False(await arbiter.MaintainerShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }
}