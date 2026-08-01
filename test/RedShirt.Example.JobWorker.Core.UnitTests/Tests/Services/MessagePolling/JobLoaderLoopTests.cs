using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.Loader;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.MessagePolling;

public class JobLoaderLoopTests
{
    [Fact]
    public async Task RunAsync_WhenAlreadyStopping_ReportsStartAndStopWithoutCallingIteration()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        // Strict + no DelayAsync setup: any sleep would fail the test.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var iterationCalls = 0;

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 5}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(_ =>
        {
            iterationCalls++;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        Assert.Equal(0, iterationCalls);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenIterationSucceedsThenStopping_InvokesIterationOnceAndReturnsFinished()
    {
        var callLog = new List<string>();

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService
            .Setup(s => s.ReportLoaderStart())
            .Callback(() => callLog.Add("Start"));
        jobLoaderStateService
            .Setup(s => s.ReportLoaderStop())
            .Callback(() => callLog.Add("Stop"));

        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 5}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(ct =>
        {
            callLog.Add("Iteration");
            keepRunning = false;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        Assert.Equal(["Start", "Iteration", "Stop"], callLog);
    }

    [Fact]
    public async Task RunAsync_WhenAbortJobLoaderLoopException_ReturnsFinishedAndStillReportsStop()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 5}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(
            _ => throw new AbortJobLoaderLoopException(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenNoJobExceptionAndStopping_ReturnsFinishedWithoutSleep()
    {
        // Handle predicate re-checks ShouldKeepRunning; when false, NoJobException escapes Polly
        // and is swallowed by the outer catch (SIGTERM path).
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 5}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(_ =>
        {
            keepRunning = false;
            throw new NoJobException();
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenReasonToWaitThenSucceeds_RetriesWithExponentialBackoffThenStops()
    {
        // Polly classic RetryForeverAsync uses 1-based retryAttempt → 2^1, 2^2.
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var remainingWaits = 2;

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 30}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(_ =>
        {
            if (remainingWaits-- > 0)
            {
                throw new NoJobException();
            }

            keepRunning = false;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            Times.Once);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken),
            Times.Once);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_WhenBackoffExceedsCap_DelaysUsingEffectiveMaxIdleWaitSeconds()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        // Cap = 3 → delays: min(3,2)=2, min(3,4)=3, min(3,8)=3
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var remainingWaits = 3;

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 3}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(_ =>
        {
            if (remainingWaits-- > 0)
            {
                throw new NoJobException();
            }

            keepRunning = false;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            Times.Once);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken),
            Times.Exactly(2));
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task RunAsync_WhenMaxIdleWaitSecondsBelowOne_UsesEffectiveMaxOfOneSecond()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        // EffectiveMaxIdleWaitSeconds = max(1, 0) = 1 → first retry delay min(1, 2^1) = 1s
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var remainingWaits = 1;

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 0}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(_ =>
        {
            if (remainingWaits-- > 0)
            {
                throw new NoJobException();
            }

            keepRunning = false;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken),
            Times.Once);
        sleepService.Verify(
            s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenMultipleSuccessfulIterations_RunsUntilStopping()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var keepRunning = true;
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(() => keepRunning);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var iterationCalls = 0;

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 5}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var result = await loop.RunAsync(_ =>
        {
            iterationCalls++;
            if (iterationCalls >= 3)
            {
                keepRunning = false;
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HandlerResponseEnum.Finished, result);
        Assert.Equal(3, iterationCalls);
    }

    [Fact]
    public async Task RunAsync_WhenUnexpectedException_PropagatesAndStillReportsStop()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(true);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var loop = new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = 5}),
            sleepService.Object,
            new NullLogger<JobLoaderLoop>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loop.RunAsync(
                _ => throw new InvalidOperationException("boom"),
                TestContext.Current.CancellationToken));

        Assert.Equal("boom", ex.Message);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }
}
