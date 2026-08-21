using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs.Subscriptions;

public class JobSubscriberManagerTests
{
    private static IJobSourceResponse CreateResponse()
    {
        return new Mock<IJobSourceResponse>(MockBehavior.Strict).Object;
    }

    [Fact]
    public async Task RunAsync_WhenNotSubscriptionSource_ReturnsNotEnabledWithoutSideEffects()
    {
        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(false);
        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var manager = new JobSubscriberManager(
            jobLoaderStateService.Object,
            jobSource.Object,
            intakeQueue.Object,
            jobIntakeService.Object);

        var result = await manager.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.NotEnabled, result);
        Assert.Empty(jobLoaderStateService.Invocations);
        Assert.Empty(intakeQueue.Invocations);
        Assert.Empty(jobIntakeService.Invocations);
        jobSource.Verify(s => s.StartSubscriberAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenSubscriptionSourceAndQueueEmpty_StartsSubscriberThenFinishes()
    {
        var callLog = new List<string>();

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService
            .Setup(s => s.ReportLoaderStart())
            .Callback(() => callLog.Add("ReportLoaderStart"));
        jobLoaderStateService
            .Setup(s => s.ReportLoaderStop())
            .Callback(() => callLog.Add("ReportLoaderStop"));

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(true);
        jobSource
            .Setup(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken))
            .Callback(() => callLog.Add("StartSubscriber"))
            .Returns(Task.CompletedTask);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        intakeQueue
            .Setup(q => q.GetNextAsync(TestContext.Current.CancellationToken))
            .Callback(() => callLog.Add("GetNext"))
            .ReturnsAsync((IJobSourceResponse?) null);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var manager = new JobSubscriberManager(
            jobLoaderStateService.Object,
            jobSource.Object,
            intakeQueue.Object,
            jobIntakeService.Object);

        var result = await manager.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        Assert.Equal(["ReportLoaderStart", "StartSubscriber", "GetNext", "ReportLoaderStop"], callLog);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenQueueYieldsResponses_SubmitsEachThenFinishes()
    {
        var first = CreateResponse();
        var second = CreateResponse();
        var submitted = new List<IJobSourceResponse>();

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(true);
        jobSource
            .Setup(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        intakeQueue
            .SetupSequence(q => q.GetNextAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(first)
            .ReturnsAsync(second)
            .ReturnsAsync((IJobSourceResponse?) null);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), TestContext.Current.CancellationToken))
            .Callback<IJobSourceResponse, CancellationToken>((response, _) => submitted.Add(response))
            .Returns(Task.CompletedTask);

        var manager = new JobSubscriberManager(
            jobLoaderStateService.Object,
            jobSource.Object,
            intakeQueue.Object,
            jobIntakeService.Object);

        var result = await manager.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandlerComponentResponse.Finished, result);
        Assert.Equal([first, second], submitted);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        intakeQueue.Verify(q => q.GetNextAsync(TestContext.Current.CancellationToken), Times.Exactly(3));
    }

    [Fact]
    public async Task RunAsync_WhenStartSubscriberThrows_ReportsStopAndPropagates()
    {
        var unexpected = new InvalidOperationException("subscriber failed");

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(true);
        jobSource
            .Setup(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var manager = new JobSubscriberManager(
            jobLoaderStateService.Object,
            jobSource.Object,
            intakeQueue.Object,
            jobIntakeService.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        jobLoaderStateService.Verify(s => s.ReportLoaderStart(), Times.Once);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        Assert.Empty(intakeQueue.Invocations);
        Assert.Empty(jobIntakeService.Invocations);
    }

    [Fact]
    public async Task RunAsync_WhenGetNextThrows_ReportsStopAndPropagates()
    {
        var unexpected = new InvalidOperationException("queue failed");

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(true);
        jobSource
            .Setup(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        intakeQueue
            .Setup(q => q.GetNextAsync(TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);

        var manager = new JobSubscriberManager(
            jobLoaderStateService.Object,
            jobSource.Object,
            intakeQueue.Object,
            jobIntakeService.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        jobIntakeService.Verify(
            s => s.SubmitAsync(It.IsAny<IJobSourceResponse>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenSubmitThrows_ReportsStopAndPropagates()
    {
        var response = CreateResponse();
        var unexpected = new InvalidOperationException("submit failed");

        var jobLoaderStateService = new Mock<IJobLoaderStateService>(MockBehavior.Strict);
        jobLoaderStateService.Setup(s => s.ReportLoaderStart());
        jobLoaderStateService.Setup(s => s.ReportLoaderStop());

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.SetupGet(s => s.IsSubscriptionSource).Returns(true);
        jobSource
            .Setup(s => s.StartSubscriberAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var intakeQueue = new Mock<IJobSubscriberIntakeQueue>(MockBehavior.Strict);
        intakeQueue
            .Setup(q => q.GetNextAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(response);

        var jobIntakeService = new Mock<IJobIntakeService>(MockBehavior.Strict);
        jobIntakeService
            .Setup(s => s.SubmitAsync(response, TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var manager = new JobSubscriberManager(
            jobLoaderStateService.Object,
            jobSource.Object,
            intakeQueue.Object,
            jobIntakeService.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.RunAsync(TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        jobLoaderStateService.Verify(s => s.ReportLoaderStop(), Times.Once);
        intakeQueue.Verify(q => q.GetNextAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}
