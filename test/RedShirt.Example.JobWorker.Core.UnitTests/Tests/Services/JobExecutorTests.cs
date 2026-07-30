using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class JobExecutorTests
{
    [Theory(Timeout = 2000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteSingleJob(bool safeRunnerSuccess)
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());

        var jobRepositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        jobRepositoryEntry.Setup(j => j.JobModel).Returns(jobModel.Object);
        jobRepositoryEntry.Setup(j => j.AcquireLockAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(Guid.NewGuid());
        jobRepositoryEntry.Setup(j => j.ReleaseLockAsync(It.IsAny<Guid>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        jobRepositoryEntry.SetupSet(j => j.State = JobState.Active);
        jobRepositoryEntry.SetupSet(j => j.State = JobState.Complete);

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>();

        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
            {
                // ReSharper disable once InvertIf
                if (!doQuit)
                {
                    // Should only be invoked twice
                    // First to tee up the exit, and a second time to exit
                    doQuit = true;
                    return true;
                }

                return false;
            });

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(jobRepositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(jobRepositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);
        safeJobRunner.Setup(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(safeRunnerSuccess);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            safeJobRunner.Object, safeAcknowledgementService.Object, new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        Assert.Single(safeJobRunner.Invocations);
        safeJobRunner.Verify(s => s.RunSafelyAsync(jobModel.Object, TestContext.Current.CancellationToken), Times.Once);

        Assert.Single(safeAcknowledgementService.Invocations);
        safeAcknowledgementService.Verify(
            s => s.AcknowledgeSafelyAsync(jobModel.Object, safeRunnerSuccess, TestContext.Current.CancellationToken),
            Times.Once);

        jobRepositoryEntry.Verify(j => j.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Once);
        jobRepositoryEntry.Verify(j => j.ReleaseLockAsync(It.IsAny<Guid>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact(Timeout = 500)]
    public async Task PrepareToExitOnNull()
    {
        var doQuit = false;
        var executionEndArbiter = new Mock<IAppliedExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ExecutorsShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
            {
                if (doQuit)
                {
                    return false;
                }

                doQuit = true;
                return true;
            });

        var safeAcknowledgementService = new Mock<ISafeJobAcknowledgementService>();

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((IJobRepositoryEntry?) null);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);

        var executor = new JobExecutor(executionEndArbiter.Object, jobRepository.Object,
            safeJobRunner.Object, safeAcknowledgementService.Object, new NullLogger<JobExecutor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        Assert.Empty(safeJobRunner.Invocations);
        Assert.Empty(safeAcknowledgementService.Invocations);

        Assert.Single(jobRepository.Invocations);
        jobRepository.Verify(r => r.GetNextJobAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}