using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class ExecutorExecutionEndArbiterTests
{
    private static Mock<IJobRepository> CreateJobRepository(
        out JobCountNotifier notifier,
        int inactiveCount = 0,
        int blockedCount = 0)
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
            .Setup(r => r.SubscribeToIdempotencyBlockedCountUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback =>
            {
                captured.NotifyBlocked = callback;
                callback(blockedCount);
            });
        return jobRepository;
    }

    private static ExecutorExecutionEndArbiter CreateArbiter(IExecutionEndArbiter inner, IJobRepository jobRepository)
    {
        return new ExecutorExecutionEndArbiter(jobRepository, inner);
    }

    [Fact]
    public void CountCallbacks_UpdateKeepRunningDecisions()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(false);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out var notifier, 1, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
        notifier.NotifyInactive(0);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
        notifier.NotifyBlocked(0);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
        notifier.NotifyInactive(2);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerFalseAndJobsPresent_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(false);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerFalseAndNoJobs_ReturnsFalse()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(false);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _).Object);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndBothCountsPositive_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndNoBlockedJobs_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndNoInactive_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 0, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndNoJobs_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    private sealed class JobCountNotifier
    {
        public Action<int> NotifyBlocked { get; set; } = _ => { };
        public Action<int> NotifyInactive { get; set; } = _ => { };
    }
}