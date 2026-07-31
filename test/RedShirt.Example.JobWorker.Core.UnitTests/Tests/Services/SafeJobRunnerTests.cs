using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class SafeJobRunnerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Test_Run_True_Basic(int internalRetryCount)
    {
        var logicRunner = new Mock<IJobLogicRunner>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var safeRunner = new SafeJobRunner(logicRunner.Object, failureHandler.Object, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = internalRetryCount
            }));

        var jobData = new Mock<IJobDataModel>();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns($"basic-message-{internalRetryCount}");
        job.Setup(j => j.IdempotencyId).Returns($"basic-idempotency-{internalRetryCount}");
        job.Setup(j => j.CreatedAtUtc).Returns(new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        job.Setup(j => j.Data).Returns(jobData.Object);

        var result = await safeRunner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);
        Assert.True(result);

        Assert.Single(logicRunner.Invocations);
        logicRunner.Verify(
            l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken), Times.Once);

        Assert.Empty(failureHandler.Invocations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Test_Run_True_Failure(int retryCount)
    {
        var logicRunner = new Mock<IJobLogicRunner>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var safeRunner = new SafeJobRunner(logicRunner.Object, failureHandler.Object, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = retryCount
            }));

        var jobData = new Mock<IJobDataModel>();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns($"failure-message-{retryCount}");
        job.Setup(j => j.IdempotencyId).Returns($"failure-idempotency-{retryCount}");
        job.Setup(j => j.CreatedAtUtc).Returns(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        job.Setup(j => j.Data).Returns(jobData.Object);

        logicRunner
            .Setup(l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken))
            .Returns((IJobModel _, CancellationToken _) => throw new JobRetryException());

        var result = await safeRunner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);
        Assert.False(result);

        Assert.Equal(retryCount + 1, logicRunner.Invocations.Count);
        logicRunner.Verify(
            l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken), Times.Exactly(retryCount + 1));

        Assert.Single(failureHandler.Invocations);
        failureHandler.Verify(
            f => f.HandleFailureAsync(job.Object, It.IsAny<JobRetryException>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    /// <summary>
    ///     The safety wrapper should also be able to tolerate a failed run of the failure handler.
    /// </summary>
    /// <param name="retryCount"></param>
    /// <exception cref="JobRetryException"></exception>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Test_Run_True_Failure_Failed(int retryCount)
    {
        var logicRunner = new Mock<IJobLogicRunner>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var safeRunner = new SafeJobRunner(logicRunner.Object, failureHandler.Object, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = retryCount
            }));

        var jobData = new Mock<IJobDataModel>();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns($"failure-failed-message-{retryCount}");
        job.Setup(j => j.IdempotencyId).Returns($"failure-failed-idempotency-{retryCount}");
        job.Setup(j => j.CreatedAtUtc).Returns(new DateTime(2024, 4, 5, 6, 7, 8, DateTimeKind.Utc));
        job.Setup(j => j.Data).Returns(jobData.Object);

        logicRunner
            .Setup(l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken))
            .Returns((IJobModel _, CancellationToken _) => throw new JobRetryException());

        failureHandler.Setup(f =>
                f.HandleFailureAsync(job.Object, It.IsAny<JobRetryException>(), TestContext.Current.CancellationToken))
            .Throws(new Exception("BOOM"));

        var result = await safeRunner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);
        Assert.False(result);

        Assert.Equal(retryCount + 1, logicRunner.Invocations.Count);
        logicRunner.Verify(
            l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken), Times.Exactly(retryCount + 1));

        Assert.Single(failureHandler.Invocations);
        failureHandler.Verify(
            f => f.HandleFailureAsync(job.Object, It.IsAny<JobRetryException>(), TestContext.Current.CancellationToken),
            Times.Once);
    }

    /// <summary>
    ///     Test that the job shall be retried due to a JobRetryException and then succeed on a later attempt.
    /// </summary>
    /// <param name="failuresBeforeSuccess"></param>
    /// <param name="internalRetryCount"></param>
    /// <exception cref="JobRetryException"></exception>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task Test_Run_True_Retry(int failuresBeforeSuccess, int internalRetryCount)
    {
        var logicRunner = new Mock<IJobLogicRunner>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var safeRunner = new SafeJobRunner(logicRunner.Object, failureHandler.Object, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = internalRetryCount
            }));

        var jobData = new Mock<IJobDataModel>();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns($"retry-message-{failuresBeforeSuccess}-{internalRetryCount}");
        job.Setup(j => j.IdempotencyId).Returns($"retry-idempotency-{failuresBeforeSuccess}-{internalRetryCount}");
        job.Setup(j => j.CreatedAtUtc).Returns(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        job.Setup(j => j.Data).Returns(jobData.Object);

        var remainingFailures = failuresBeforeSuccess;

        logicRunner
            .Setup(l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken))
            .Returns((IJobModel _, CancellationToken _) =>
            {
                if (remainingFailures > 0)
                {
                    remainingFailures--;
                    throw new JobRetryException();
                }

                return Task.CompletedTask;
            });

        var result = await safeRunner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);
        Assert.True(result);

        Assert.Equal(failuresBeforeSuccess + 1, logicRunner.Invocations.Count);
        logicRunner.Verify(
            l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken), Times.Exactly(failuresBeforeSuccess + 1));

        Assert.Empty(failureHandler.Invocations);
    }

    /// <summary>
    ///     Test that the job shall be retried due to a JobRetryException and then succeed on a later attempt.
    ///     The JobRetryException shall request a delay before retrying.
    /// </summary>
    /// <param name="delayMilliseconds"></param>
    /// <exception cref="JobRetryException"></exception>
    [Theory]
    [InlineData(250)]
    [InlineData(500)]
    public async Task Test_Run_True_Retry_WithDelay(int delayMilliseconds)
    {
        var logicRunner = new Mock<IJobLogicRunner>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var safeRunner = new SafeJobRunner(logicRunner.Object, failureHandler.Object, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 2
            }));

        var jobData = new Mock<IJobDataModel>();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.MessageId).Returns($"retry-delay-message-{delayMilliseconds}");
        job.Setup(j => j.IdempotencyId).Returns($"retry-delay-idempotency-{delayMilliseconds}");
        job.Setup(j => j.CreatedAtUtc).Returns(new DateTime(2024, 6, 7, 8, 9, 10, DateTimeKind.Utc));
        job.Setup(j => j.Data).Returns(jobData.Object);

        var queue = new Queue<object?>();
        queue.Enqueue(job.Object);

        logicRunner
            .Setup(l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken))
            .Returns((IJobModel _, CancellationToken _) =>
            {
                if (queue.TryDequeue(out _))
                {
                    throw new JobRetryException
                    {
                        DelayTimeMilliseconds = delayMilliseconds
                    };
                }

                return Task.CompletedTask;
            });

        var stopwatch = Stopwatch.StartNew();
        var result = await safeRunner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);
        stopwatch.Stop();
        Assert.True(result);

        Assert.True(stopwatch.ElapsedMilliseconds >= delayMilliseconds - 50); // 50ms grace

        Assert.Equal(2, logicRunner.Invocations.Count);
        logicRunner.Verify(
            l => l.RunAsync(
                It.Is<IJobModel>(m =>
                    !ReferenceEquals(m, job.Object) &&
                    m.MessageId == job.Object.MessageId &&
                    m.IdempotencyId == job.Object.IdempotencyId &&
                    m.CreatedAtUtc == job.Object.CreatedAtUtc &&
                    ReferenceEquals(m.Data, jobData.Object)),
                TestContext.Current.CancellationToken), Times.Exactly(2));

        Assert.Empty(failureHandler.Invocations);
    }
}