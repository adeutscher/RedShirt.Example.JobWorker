using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs.Polling;

public class JobLoaderLoopTests
{
    private static JobLoaderLoop CreateLoop(
        Mock<IJobLoaderStateService> jobLoaderStateService,
        Mock<IExecutionEndArbiter> executionEndArbiter,
        Mock<ISleepService> sleepService,
        Mock<IJobLoader> jobLoader,
        int maxIdleWaitSeconds = 5,
        bool isSubscriptionSource = false)
    {
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(isSubscriptionSource);

        return new JobLoaderLoop(
            jobLoaderStateService.Object,
            executionEndArbiter.Object,
            sleepService.Object,
            jobLoader.Object,
            jobSource.Object,
            Options.Create(new LoopOptionsConfigurationModel {MaxIdleWaitSeconds = maxIdleWaitSeconds}),
            new NullLogger<JobLoaderLoop>());
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

        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(new AbortJobLoaderLoopException());

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyStopping_ReportsStartAndStopWithoutCallingJobLoader()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.Setup(a => a.ShouldKeepRunning()).Returns(false);

        // Strict + no DelayAsync / RunAsync setup: any sleep or loader call would fail the test.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        Assert.Empty(jobLoader.Invocations);
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
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                if (remainingWaits-- > 0)
                {
                    throw new NoJobException();
                }

                keepRunning = false;
                return Task.CompletedTask;
            });

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader,
            3);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
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
    public async Task RunAsync_WhenLoaderSucceedsThenStopping_InvokesLoaderOnceAndReturnsFinished()
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

        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                callLog.Add("Loader");
                keepRunning = false;
                return Task.CompletedTask;
            });

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        Assert.Equal(["Start", "Loader", "Stop"], callLog);
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
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                if (remainingWaits-- > 0)
                {
                    throw new NoJobException();
                }

                keepRunning = false;
                return Task.CompletedTask;
            });

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader,
            0);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
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

        var loaderCalls = 0;
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                loaderCalls++;
                if (loaderCalls >= 3)
                {
                    keepRunning = false;
                }

                return Task.CompletedTask;
            });

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        Assert.Equal(3, loaderCalls);
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

        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                keepRunning = false;
                throw new NoJobException();
            });

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
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
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .Returns(() =>
            {
                if (remainingWaits-- > 0)
                {
                    throw new NoJobException();
                }

                keepRunning = false;
                return Task.CompletedTask;
            });

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader,
            30);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
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
    public async Task RunAsync_WhenSubscriptionSource_ReportsStartAndReturnsFinishedWithoutLoading()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader,
            isSubscriptionSource: true);

        var result = await loop.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Never);
        Assert.Empty(jobLoader.Invocations);
        Assert.Empty(executionEndArbiter.Invocations);
        Assert.Empty(sleepService.Invocations);
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

        var jobLoader = new Mock<IJobLoader>(MockBehavior.Strict);
        jobLoader
            .Setup(l => l.RunAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var loop = CreateLoop(jobLoaderStateService, executionEndArbiter, sleepService, jobLoader);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loop.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal("boom", ex.Message);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
    }
}