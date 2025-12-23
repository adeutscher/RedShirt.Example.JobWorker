using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums.Loader;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Models.Loader;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.Loader;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Loader;

public class ExecutorTests
{
    [Theory(Timeout = 500)]
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

        var doQuit = false;
        var executionEndArbiter = new Mock<ILoaderExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
            {
                if (!doQuit)
                {
                    doQuit = true;
                    return true;
                }

                return false;
            });

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource.Setup(s =>
                s.AcknowledgeCompletionAsync(jobModel.Object, safeRunnerSuccess, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

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

        var executor = new Executor(executionEndArbiter.Object, jobSource.Object, jobRepository.Object,
            safeJobRunner.Object, new NullLogger<Executor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        Assert.Single(jobSource.Invocations);
        jobSource.Verify(
            s => s.AcknowledgeCompletionAsync(jobModel.Object, safeRunnerSuccess,
                TestContext.Current.CancellationToken), Times.Once);

        jobRepositoryEntry.Verify(j => j.AcquireLockAsync(TestContext.Current.CancellationToken), Times.Exactly(2));
        jobRepositoryEntry.Verify(j => j.ReleaseLockAsync(It.IsAny<Guid>(), TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }

    [Fact(Timeout = 500)]
    public async Task PrepareToExitOnNull()
    {
        var doQuit = false;
        var executionEndArbiter = new Mock<ILoaderExecutionEndArbiter>(MockBehavior.Strict);
        executionEndArbiter
            .Setup(a => a.ShouldKeepRunningAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((CancellationToken _) =>
            {
                if (doQuit)
                {
                    return false;
                }

                doQuit = true;
                return true;
            });

        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync((IJobRepositoryEntry?) null);

        var safeJobRunner = new Mock<ISafeJobRunner>(MockBehavior.Strict);

        var executor = new Executor(executionEndArbiter.Object, jobSource.Object, jobRepository.Object,
            safeJobRunner.Object, new NullLogger<Executor>());

        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        Assert.Empty(jobSource.Invocations);

        Assert.Single(jobRepository.Invocations);
        jobRepository.Verify(r => r.GetNextJobAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}