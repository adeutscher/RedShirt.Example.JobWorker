using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;
using System.Net;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.UnitTests.Tests.Services.Resilience;

public class BarRetryWrapperServiceTests
{
    private static BarExceptionArbiterReport TransientReport()
    {
        return new BarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };
    }

    private static BarExceptionArbiterReport PermanentReport()
    {
        return new BarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        };
    }

    private static Mock<ISleepService> CreateSleepService(IList<TimeSpan>? capturedDelays = null)
    {
        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        sleep.Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((delay, _) =>
            {
                capturedDelays?.Add(delay);
                return Task.CompletedTask;
            });
        return sleep;
    }

    private static BarRetryWrapperService CreateSut(
        Mock<IBarExceptionArbiterService> arbiter,
        Mock<ISleepService> sleep,
        int retryCount = 3)
    {
        return new BarRetryWrapperService(arbiter.Object, NullLogger<BarRetryWrapperService>.Instance, sleep.Object,
            Options.Create(new BarRetryWrapperService.ConfigurationModel {RetryCount = retryCount}));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-2, 0)]
    [InlineData(5, 5)]
    public void ConfigurationModel_EffectiveRetryCount(int configured, int expected)
    {
        var model = new BarRetryWrapperService.ConfigurationModel {RetryCount = configured};

        Assert.Equal(expected, model.EffectiveRetryCount);
    }

    [Fact]
    public void ConfigurationModel_EffectiveRetryCount_WhenNull_UsesDefault()
    {
        var model = new BarRetryWrapperService.ConfigurationModel {RetryCount = null};

        Assert.Equal(3, model.EffectiveRetryCount);
    }

    [Fact]
    public async Task RunAsync_WhenBarReasonToWaitException_PropagatesWithoutRetryOrWrapping()
    {
        var rateLimited = new BarRateLimitedException(TimeSpan.FromSeconds(2));
        var arbiter = new Mock<IBarExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var sut = CreateSut(arbiter, sleep, 3);

        var thrown = await Assert.ThrowsAsync<BarRateLimitedException>(() =>
            sut.RunAsync<int>(_ => throw rateLimited, TestContext.Current.CancellationToken));

        Assert.Same(rateLimited, thrown);
        arbiter.VerifyNoOtherCalls();
        sleep.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenBarRecordNotFoundException_PropagatesWithoutWrapping()
    {
        var notFound = new BarRecordNotFoundException(404);
        var arbiter = new Mock<IBarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(notFound)).Returns(new BarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        });
        var sleep = CreateSleepService();
        var sut = CreateSut(arbiter, sleep, 1);

        var thrown = await Assert.ThrowsAsync<BarRecordNotFoundException>(() =>
            sut.RunAsync<int>(_ => throw notFound, TestContext.Current.CancellationToken));

        Assert.Same(notFound, thrown);
        arbiter.Verify(a => a.GetReport(notFound), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenFuncSucceeds_ReturnsResult()
    {
        var arbiter = new Mock<IBarExceptionArbiterService>(MockBehavior.Strict);
        var sleep = CreateSleepService();
        var sut = CreateSut(arbiter, sleep);

        var result = await sut.RunAsync(_ => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_WhenPermanentExpectedFailure_WrapsInBarException()
    {
        var inner = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
        var arbiter = new Mock<IBarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(inner)).Returns(PermanentReport());
        var sleep = CreateSleepService();
        var sut = CreateSut(arbiter, sleep, 1);

        var thrown = await Assert.ThrowsAsync<BarException>(() =>
            sut.RunAsync<int>(_ => throw inner, TestContext.Current.CancellationToken));

        Assert.True(thrown.IsHandled);
        Assert.False(thrown.CouldBeTransient);
        Assert.Same(inner, thrown.InnerException);
    }

    [Fact]
    public async Task RunAsync_WhenTransientFailureThenSuccess_RetriesWithExponentialBackoff()
    {
        var attempts = 0;
        var capturedDelays = new List<TimeSpan>();
        var arbiter = new Mock<IBarExceptionArbiterService>(MockBehavior.Strict);
        arbiter.Setup(a => a.GetReport(It.IsAny<Exception>())).Returns(TransientReport());
        var sleep = CreateSleepService(capturedDelays);
        var sut = CreateSut(arbiter, sleep);

        var result = await sut.RunAsync(_ =>
        {
            if (++attempts == 1)
            {
                throw new HttpRequestException("transient", null, HttpStatusCode.ServiceUnavailable);
            }

            return Task.FromResult("ok");
        }, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
        Assert.Equal([TimeSpan.FromSeconds(1)], capturedDelays);
    }
}