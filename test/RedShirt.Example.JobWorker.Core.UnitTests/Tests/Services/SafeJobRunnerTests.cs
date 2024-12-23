using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class SafeJobRunnerTests
{
    [Fact]
    public async Task Test_Run_True_Basic()
    {
        var logicRunner = new Mock<IJobLogicRunner>();
        var failureHandler = new Mock<IJobFailureHandler>();

        var safeRunner = new SafeJobRunner(logicRunner.Object, failureHandler.Object, new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0
            }));

        var jobData = new Mock<IJobDataModel>();
        var job = new Mock<IJobModel>();
        job.Setup(j => j.Data)
            .Returns(jobData.Object);

        var cts = new CancellationTokenSource();
        var result = await safeRunner.RunSafelyAsync(job.Object, cts.Token);
        Assert.True(result);

        Assert.Single(logicRunner.Invocations);
        logicRunner.Verify(l => l.RunAsync(jobData.Object, cts.Token), Times.Once);

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
        job.Setup(j => j.Data)
            .Returns(jobData.Object);

        var cts = new CancellationTokenSource();

        logicRunner.Setup(l => l.RunAsync(jobData.Object, cts.Token))
            .Returns((IJobDataModel _, CancellationToken _) => throw new JobRetryException());

        var result = await safeRunner.RunSafelyAsync(job.Object, cts.Token);
        Assert.False(result);

        Assert.Equal(retryCount + 1, logicRunner.Invocations.Count);
        logicRunner.Verify(l => l.RunAsync(jobData.Object, cts.Token), Times.Exactly(retryCount + 1));

        Assert.Single(failureHandler.Invocations);
        failureHandler.Verify(f => f.HandleFailureAsync(job.Object, It.IsAny<JobRetryException>(), cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task Test_Run_True_Retry()
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
        job.Setup(j => j.Data)
            .Returns(jobData.Object);

        var cts = new CancellationTokenSource();

        var queue = new Queue<object?>();
        queue.Enqueue(job.Object);

        logicRunner.Setup(l => l.RunAsync(jobData.Object, cts.Token))
            .Returns((IJobDataModel _, CancellationToken _) =>
            {
                if (queue.TryDequeue(out _))
                {
                    throw new JobRetryException();
                }

                return Task.CompletedTask;
            });

        var result = await safeRunner.RunSafelyAsync(job.Object, cts.Token);
        Assert.True(result);

        Assert.Equal(2, logicRunner.Invocations.Count);
        logicRunner.Verify(l => l.RunAsync(jobData.Object, cts.Token), Times.Exactly(2));

        Assert.Empty(failureHandler.Invocations);
    }
}