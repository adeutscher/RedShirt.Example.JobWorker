using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.IntegrationTests.Tests;

/// <summary>
///     Integration coverage for the path from <see cref="IJobLogicRunner" /> through
///     <see cref="JobExecutor" />, <see cref="SafeJobRunner" />, and <see cref="SafeJobAcknowledgementService" />
///     into <see cref="IJobSource" /> / <see cref="IJobFailureHandler" /> / <see cref="ICoreStatisticsService" />.
/// </summary>
public class JobResultTranslationTests
{
    private static ISleepService CreateSleepService()
    {
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return sleepService.Object;
    }

    private static Mock<IAbstractedLock> CreateAcquiredIdempotencyLock()
    {
        var idempotencyLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        idempotencyLock.SetupGet(l => l.IsAcquired).Returns(true);
        idempotencyLock.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return idempotencyLock;
    }

    /// <summary>
    ///     Verifies that a <see cref="JobResult" /> returned by <see cref="IJobLogicRunner" /> is translated into the
    ///     matching <see cref="CoreJobResult" /> passed to <see cref="IJobSource" />, recorded on
    ///     <see cref="ICoreStatisticsService" />, and—when unsuccessful—the matching <see cref="FailureType" />
    ///     passed to <see cref="IJobFailureHandler" />, when execution is driven by <see cref="JobExecutor.RunAsync" />.
    /// </summary>
    [Theory(Timeout = 2000)]
    [InlineData(JobResult.Success, CoreJobResult.Success, null)]
    [InlineData(JobResult.Failure, CoreJobResult.Failure, FailureType.Execution)]
    [InlineData(JobResult.InvalidData, CoreJobResult.InvalidData, FailureType.Broken)]
    public async Task JobResult_FromLogicRunner_IsTranslated_ToJobSource_AndFailureHandler(
        JobResult jobResult,
        CoreJobResult expectedCoreJobResult,
        FailureType? expectedFailureType)
    {
        // Job models / repository entry
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.MessageId).Returns(Guid.NewGuid().ToString());
        var rawJob = new Mock<IRawJobModel>(MockBehavior.Strict);
        var repositoryEntry = new Mock<IJobRepositoryEntry>(MockBehavior.Strict);
        repositoryEntry.Setup(e => e.JobModel).Returns(job.Object);
        repositoryEntry.Setup(e => e.RawJobModel).Returns(rawJob.Object);
        repositoryEntry
            .Setup(e => e.SetStateAsync(JobState.Complete, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Application logic
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobLogicRunnerResponse {Result = jobResult});

        // Job source acknowledgement
        var jobSource = new Mock<IJobSource>(MockBehavior.Strict);
        jobSource
            .Setup(s => s.AcknowledgeAsync(rawJob.Object, expectedCoreJobResult,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Failure handling (invoked only for non-success results)
        var failureHandler = new Mock<IJobFailureHandler>(MockBehavior.Strict);
        if (expectedFailureType is { } failureType)
        {
            failureHandler
                .Setup(h => h.HandleFailureAsync(rawJob.Object, failureType, null,
                    TestContext.Current.CancellationToken))
                .Returns(Task.CompletedTask);
        }

        // Statistics: expect the translated CoreJobResult
        var statisticsService = new Mock<ICoreStatisticsService>(MockBehavior.Strict);
        statisticsService.Setup(s => s.RecordResult(expectedCoreJobResult, It.IsAny<TimeSpan>()));

        // Executor loop control: process one job, then stop
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

        // Job repository: yield the single prepared entry
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetNextJobAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(repositoryEntry.Object);
        jobRepository
            .Setup(r => r.RemoveJobAsync(repositoryEntry.Object, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Idempotency: allow execution with no prior cached result
        var idempotencyLock = CreateAcquiredIdempotencyLock();
        var idempotencyExecutionService = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotencyExecutionService
            .Setup(s => s.GetLockAsync(job.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync(idempotencyLock.Object);
        idempotencyExecutionService
            .Setup(s => s.GetCachedResultAsync(job.Object, TestContext.Current.CancellationToken))
            .ReturnsAsync((IdempotencyCacheResult?) null);
        idempotencyExecutionService
            .Setup(s => s.SetResultInCacheAsync(rawJob.Object, expectedCoreJobResult,
                It.IsAny<ISafeAcknowledgementResult>(), TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // System under test: real executor + runner + acknowledgement path
        var sleepService = CreateSleepService();
        var safeJobRunner = new SafeJobRunner(
            logicRunner.Object,
            sleepService,
            new TimeBorderWrapperService(
                sleepService,
                Options.Create(new TimeBorderWrapperService.ConfigurationModel
                {
                    TaskWaitBufferSeconds = null,
                    TruantAlertIntervalSeconds = 30
                }),
                NullLogger<TimeBorderWrapperService>.Instance),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = null
            }));
        var acknowledgementService = new SafeJobAcknowledgementService(
            jobSource.Object,
            failureHandler.Object,
            sleepService,
            Mock.Of<ICoreHealthStateUpdateService>(),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            new NullLogger<SafeJobAcknowledgementService>());
        var executor = new JobExecutor(
            executionEndArbiter.Object,
            jobRepository.Object,
            idempotencyExecutionService.Object,
            safeJobRunner,
            acknowledgementService,
            statisticsService.Object,
            new NullLogger<JobExecutor>());

        // Run
        await executor.RunAsync(0, TestContext.Current.CancellationToken);

        // Verify
        jobSource.Verify(
            s => s.AcknowledgeAsync(rawJob.Object, expectedCoreJobResult, TestContext.Current.CancellationToken),
            Times.Once);
        statisticsService.Verify(
            s => s.RecordResult(expectedCoreJobResult, It.IsAny<TimeSpan>()),
            Times.Once);

        if (expectedFailureType is { } expected)
        {
            failureHandler.Verify(
                h => h.HandleFailureAsync(rawJob.Object, expected, null, TestContext.Current.CancellationToken),
                Times.Once);
        }
        else
        {
            failureHandler.Verify(
                h => h.HandleFailureAsync(It.IsAny<IRawJobModel>(), It.IsAny<FailureType>(), It.IsAny<Exception?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}