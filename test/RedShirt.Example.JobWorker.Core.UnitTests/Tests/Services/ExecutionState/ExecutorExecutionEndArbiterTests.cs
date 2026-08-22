using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class ExecutorExecutionEndArbiterTests
{
    private static Mock<IJobRepository> CreateJobRepository(out Action<int> notifyInactive, int inactiveCount = 0)
    {
        Action<int> captured = _ => { };
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.SubscribeToInactiveCountUpdate(It.IsAny<Action<int>>()))
            .Callback<Action<int>>(callback =>
            {
                captured = callback;
                callback(inactiveCount);
            });
        notifyInactive = count => captured(count);
        return jobRepository;
    }

    private static ExecutorExecutionEndArbiter CreateArbiter(IExecutionEndArbiter inner, IJobRepository jobRepository)
    {
        return new ExecutorExecutionEndArbiter(jobRepository, inner,
            new Mock<ISleepService>(MockBehavior.Strict).Object,
            NullLogger<ExecutorExecutionEndArbiter>.Instance);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1).Object);
        arbiter.Dispose();
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerFalseAndInactiveJobs_ReturnsFalse()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(false);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1).Object);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndInactiveJobs_ReturnsTrue()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void ExecutorsShouldKeepRunning_WhenInnerTrueAndNoInactive_ReturnsFalse()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out _, 0).Object);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
    }

    [Fact]
    public void InactiveCountCallback_UpdatesKeepRunningDecision()
    {
        var inner = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        inner.Setup(a => a.ShouldKeepRunning()).Returns(true);
        using var arbiter = CreateArbiter(inner.Object, CreateJobRepository(out var notify, 1).Object);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
        notify(0);
        Assert.False(arbiter.ExecutorsShouldKeepRunning());
        notify(2);
        Assert.True(arbiter.ExecutorsShouldKeepRunning());
    }
}
