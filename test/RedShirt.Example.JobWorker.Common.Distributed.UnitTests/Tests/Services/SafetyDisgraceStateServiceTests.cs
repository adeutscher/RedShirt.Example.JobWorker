using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class SafetyDisgraceStateServiceTests
{
    [Fact(Timeout = 3000)]
    public async Task EnterDisgracePeriod_ResetsDisgraceWindowFromLatestEntry()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 1
            }));

        service.EnterDisgracePeriod();
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        Assert.True(service.IsInDisgracePeriod(out _));

        // Re-enter near the end of the first window; the period should extend from now.
        service.EnterDisgracePeriod();
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        Assert.True(service.IsInDisgracePeriod(out _));

        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        Assert.False(service.IsInDisgracePeriod(out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(60)]
    public void EnterDisgracePeriod_WithPositiveDuration_EntersDisgrace(int disgracePeriodSeconds)
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = disgracePeriodSeconds
            }));

        service.EnterDisgracePeriod();

        Assert.True(service.IsInDisgracePeriod(out _));
    }

    [Fact]
    public void EnterDisgracePeriod_WithZeroSeconds_DoesNotRemainInDisgrace()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 0
            }));

        service.EnterDisgracePeriod();

        Assert.False(service.IsInDisgracePeriod(out _));
    }

    [Fact]
    public void GetNextAttemptTime_WhenInDisgrace_ReturnsDisgraceEnd()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 60
            }));

        var before = DateTime.UtcNow;
        service.EnterDisgracePeriod();
        var nextAttempt = service.GetNextAttemptTime();
        var after = DateTime.UtcNow;

        Assert.True(service.IsInDisgracePeriod(out _));
        Assert.InRange(nextAttempt, before.AddSeconds(59), after.AddSeconds(61));
    }

    [Fact]
    public void GetNextAttemptTime_WhenNotInDisgrace_ReturnsApproximateUtcNow()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 60
            }));

        var before = DateTime.UtcNow;
        var nextAttempt = service.GetNextAttemptTime();
        var after = DateTime.UtcNow;

        Assert.InRange(nextAttempt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact(Timeout = 3000)]
    public async Task IsInDisgracePeriod_ReturnsFalseAfterPeriodElapses()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 1
            }));

        service.EnterDisgracePeriod();
        Assert.True(service.IsInDisgracePeriod(out _));

        await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);

        Assert.False(service.IsInDisgracePeriod(out _));
    }

    [Fact]
    public void IsInDisgracePeriod_WhenInDisgrace_ReturnsDisgraceEndAsNextAttemptTime()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 60
            }));

        var before = DateTime.UtcNow;
        service.EnterDisgracePeriod();
        var inDisgrace = service.IsInDisgracePeriod(out var nextAttemptTime);
        var after = DateTime.UtcNow;

        Assert.True(inDisgrace);
        Assert.InRange(nextAttemptTime, before.AddSeconds(59), after.AddSeconds(61));
    }

    [Fact]
    public void IsInDisgracePeriod_WhenNeverEntered_ReturnsFalse()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 60
            }));

        Assert.False(service.IsInDisgracePeriod(out _));
    }

    [Fact]
    public void IsInDisgracePeriod_WhenNotInDisgrace_ReturnsApproximateUtcNowAsNextAttemptTime()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 60
            }));

        var before = DateTime.UtcNow;
        var inDisgrace = service.IsInDisgracePeriod(out var nextAttemptTime);
        var after = DateTime.UtcNow;

        Assert.False(inDisgrace);
        Assert.InRange(nextAttemptTime, before.AddSeconds(-1), after.AddSeconds(1));
    }
}