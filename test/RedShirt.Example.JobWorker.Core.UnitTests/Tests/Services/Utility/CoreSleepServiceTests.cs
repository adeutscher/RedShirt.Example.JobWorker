using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Utility;

public class CoreSleepServiceTests
{
    [Fact]
    public async Task DelayAsync_PassesThroughToSleepService()
    {
        var delay = TimeSpan.FromSeconds(3);
        var token = TestContext.Current.CancellationToken;

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(delay, token))
            .Returns(Task.CompletedTask);

        var service = new CoreSleepService(executionEndArbiter.Object, sleepService.Object);

        await service.DelayAsync(delay, token);

        sleepService.Verify(s => s.DelayAsync(delay, token), Times.Once);
    }

    [Fact]
    public async Task DelayWithStopAwareness_CompletesNormallyWhenNeitherTokenCancels()
    {
        var delay = TimeSpan.FromSeconds(5);
        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.SetupGet(a => a.CancellationToken).Returns(CancellationToken.None);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new CoreSleepService(executionEndArbiter.Object, sleepService.Object);

        await service.DelayWithStopAwareness(delay, TestContext.Current.CancellationToken);

        sleepService.Verify(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DelayWithStopAwareness_WhenStopped_SwallowsCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var stopCts = new CancellationTokenSource();
        await stopCts.CancelAsync();

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.SetupGet(a => a.CancellationToken).Returns(stopCts.Token);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) => Task.FromCanceled(token));

        var service = new CoreSleepService(executionEndArbiter.Object, sleepService.Object);

        await service.DelayWithStopAwareness(delay, CancellationToken.None);
    }

    [Fact]
    public async Task DelayWithStopAwareness_WhenCallerCancels_PropagatesCancellation()
    {
        var delay = TimeSpan.FromSeconds(5);
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        var executionEndArbiter = new Mock<IExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter.SetupGet(a => a.CancellationToken).Returns(CancellationToken.None);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(delay, It.IsAny<CancellationToken>()))
            .Returns((TimeSpan _, CancellationToken token) => Task.FromCanceled(token));

        var service = new CoreSleepService(executionEndArbiter.Object, sleepService.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DelayWithStopAwareness(delay, callerCts.Token));
    }
}
