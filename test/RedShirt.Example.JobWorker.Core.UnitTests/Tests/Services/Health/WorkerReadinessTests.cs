using Moq;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Health;

public class WorkerReadinessTests
{
    [Fact]
    public void IsReady_WhenLoaderStartedAndRunning_True()
    {
        var loaderState = new Mock<IJobLoaderStateReaderService>(MockBehavior.Strict);
        loaderState.Setup(s => s.HasLoaderStarted()).Returns(true);
        loaderState.Setup(s => s.IsLoaderFinished()).Returns(false);

        var endArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        endArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var readiness = new WorkerReadiness(loaderState.Object, endArbiter.Object);

        Assert.True(readiness.IsReady());
    }

    [Fact]
    public void IsReady_WhenLoaderNotStarted_False()
    {
        var loaderState = new Mock<IJobLoaderStateReaderService>(MockBehavior.Strict);
        loaderState.Setup(s => s.HasLoaderStarted()).Returns(false);

        var endArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        endArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var readiness = new WorkerReadiness(loaderState.Object, endArbiter.Object);

        Assert.False(readiness.IsReady());
    }

    [Fact]
    public void IsReady_WhenLoaderFinished_False()
    {
        var loaderState = new Mock<IJobLoaderStateReaderService>(MockBehavior.Strict);
        loaderState.Setup(s => s.HasLoaderStarted()).Returns(true);
        loaderState.Setup(s => s.IsLoaderFinished()).Returns(true);

        var endArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        endArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var readiness = new WorkerReadiness(loaderState.Object, endArbiter.Object);

        Assert.False(readiness.IsReady());
    }

    [Fact]
    public void IsReady_WhenShutdownSignaled_False()
    {
        var loaderState = new Mock<IJobLoaderStateReaderService>(MockBehavior.Strict);
        var endArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        endArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        var readiness = new WorkerReadiness(loaderState.Object, endArbiter.Object);

        Assert.False(readiness.IsReady());
    }
}
