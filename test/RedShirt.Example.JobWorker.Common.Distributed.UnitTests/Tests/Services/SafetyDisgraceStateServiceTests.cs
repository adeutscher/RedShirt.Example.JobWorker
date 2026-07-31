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
        Assert.True(service.IsInDisgracePeriod());

        // Re-enter near the end of the first window; the period should extend from now.
        service.EnterDisgracePeriod();
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        Assert.True(service.IsInDisgracePeriod());

        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        Assert.False(service.IsInDisgracePeriod());
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

        Assert.True(service.IsInDisgracePeriod());
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

        Assert.False(service.IsInDisgracePeriod());
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
        Assert.True(service.IsInDisgracePeriod());

        await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);

        Assert.False(service.IsInDisgracePeriod());
    }

    [Fact]
    public void IsInDisgracePeriod_WhenNeverEntered_ReturnsFalse()
    {
        var service = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = 60
            }));

        Assert.False(service.IsInDisgracePeriod());
    }
}