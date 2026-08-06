using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Exceptions;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Safety;

public class SafeJobRunnerTests
{
    private static ITimeBorderWrapperService CreatePassthroughTimeBorder()
    {
        var timeBorder = new Mock<ITimeBorderWrapperService>(MockBehavior.Strict);
        timeBorder
            .Setup(t => t.RunAsync(
                It.IsAny<IJobModel>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<IJobModel, CancellationToken, Task<JobResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((IJobModel data, TimeSpan? _,
                    Func<IJobModel, CancellationToken, Task<JobResult>> callback, CancellationToken token) =>
                callback(data, token));
        return timeBorder.Object;
    }

    [Fact]
    public async Task RunSafelyAsync_WhenInternalRetryCountIsZero_DoesNotRetryJobRetryException()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Failure, result.Result);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionExhausted_RetriesThenReturnsFailureWithException()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 2,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Failure, result.Result);
        Assert.Same(expected, result.Exception);
        // initial attempt + 2 retries
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Exactly(3));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionHasExplicitDelay_OverridesIncrementalBackoff()
    {
        const int delayMilliseconds = 250;
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var attempts = 0;

        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++attempts == 1)
                {
                    throw new JobRetryException
                    {
                        DelayTimeMilliseconds = delayMilliseconds
                    };
                }

                return Task.FromResult(JobResult.Success);
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromMilliseconds(delayMilliseconds), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 1,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Success, result.Result);
        Assert.Null(result.Exception);
        sleepService.Verify(
            s => s.DelayAsync(TimeSpan.FromMilliseconds(delayMilliseconds), It.IsAny<CancellationToken>()),
            Times.Once);
        // Must not fall through to exponential backoff (2^1 = 2s).
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(2), It.IsAny<CancellationToken>()), Times.Never);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenJobRetryExceptionThenSucceeds_ReturnsSuccessWithNullException()
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var remainingFailures = 2;

        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (remainingFailures-- > 0)
                {
                    throw new JobRetryException();
                }

                return Task.FromResult(JobResult.Success);
            });

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Success, result.Result);
        Assert.Null(result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Exactly(3));
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunSafelyAsync_WhenLogicReturnsFailure_MapsToFailureWithoutException()
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobResult.Failure);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Failure, result.Result);
        Assert.Null(result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenLogicReturnsInvalidData_MapsToInvalidDataWithoutException()
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobResult.InvalidData);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.InvalidData, result.Result);
        Assert.Null(result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenLogicSucceeds_ReturnsSuccessWithNullExceptionAndDoesNotSleep()
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobResult.Success);

        // Strict + no DelayAsync setup: any sleep would fail the test.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Success, result.Result);
        Assert.Null(result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    ///     <see langword="null" />, <c>0</c>, and negative values of
    ///     <see cref="SafeJobRunner.ConfigurationModel.MaxJobTimeSeconds" />
    ///     all disable the per-attempt time border (translated to a null <see cref="TimeSpan" />).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RunSafelyAsync_WhenMaxJobTimeSecondsNullOrNonPositive_PassesNullMaximumTimeToTimeBorder(
        int? maxJobTimeSeconds)
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JobResult.Success);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        TimeSpan? observedMaximumTime = TimeSpan.FromHours(1); // sentinel distinct from null
        var timeBorder = new Mock<ITimeBorderWrapperService>(MockBehavior.Strict);
        timeBorder
            .Setup(t => t.RunAsync(
                job.Object,
                It.IsAny<TimeSpan?>(),
                It.IsAny<Func<IJobModel, CancellationToken, Task<JobResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((IJobModel data, TimeSpan? maximumTime,
                Func<IJobModel, CancellationToken, Task<JobResult>> callback, CancellationToken token) =>
            {
                observedMaximumTime = maximumTime;
                return callback(data, token);
            });

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            timeBorder.Object,
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 0,
                MaxJobTimeSeconds = maxJobTimeSeconds
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Success, result.Result);
        Assert.Null(observedMaximumTime);
        timeBorder.Verify(
            t => t.RunAsync(
                job.Object,
                null,
                It.IsAny<Func<IJobModel, CancellationToken, Task<JobResult>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenNegativeInternalRetryCount_TreatedAsZeroAndDoesNotRetry()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = -1,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Failure, result.Result);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task RunSafelyAsync_WhenOperationCancelled_ReturnsCancelledWithSameException(Type exceptionType)
    {
        var expected = (Exception) Activator.CreateInstance(exceptionType)!;
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Cancelled, result.Result);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenOrdinaryException_ReturnsFailureWithSameExceptionWithoutRetryOrSleep()
    {
        var expected = new InvalidOperationException("boom");
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        // Retries are configured, but only JobRetryException is handled — sleep must never run.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Failure, result.Result);
        Assert.Same(expected, result.Exception);
        logicRunner.Verify(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSafelyAsync_WhenRetryingWithoutExplicitDelay_UsesIncrementalBackoff()
    {
        var expected = new JobRetryException();
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        var logicRunner = new Mock<IJobLogicRunner>(MockBehavior.Strict);
        logicRunner
            .Setup(l => l.RunAsync(job.Object, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        // Polly v8 AttemptNumber is 0-based; +1 → 2^1, 2^2, 2^3.
        var sleepService = new Mock<ISleepService>(MockBehavior.Strict);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(2), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(4), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sleepService
            .Setup(s => s.DelayAsync(TimeSpan.FromSeconds(8), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new SafeJobRunner(
            logicRunner.Object,
            sleepService.Object,
            CreatePassthroughTimeBorder(),
            new NullLogger<SafeJobRunner>(),
            Options.Create(new SafeJobRunner.ConfigurationModel
            {
                InternalRetryCount = 3,
                MaxJobTimeSeconds = null
            }));

        var result = await runner.RunSafelyAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Equal(CoreJobResult.Failure, result.Result);
        Assert.Same(expected, result.Exception);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(2), It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(4), It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(TimeSpan.FromSeconds(8), It.IsAny<CancellationToken>()), Times.Once);
        sleepService.Verify(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}