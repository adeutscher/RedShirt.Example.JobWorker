using RedShirt.Example.JobWorker.Common.Distributed.Configuration;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Configuration;

public class LockConfigurationModelTests
{
    [Theory]
    [InlineData(null, 10)]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    [InlineData(60, 60)]
    public void EffectiveTimeout_UsesDefaultClampsToMinimumOfOne(int? timeoutSeconds, int expectedSeconds)
    {
        var model = new LockConfigurationModel
        {
            TimeoutSeconds = timeoutSeconds
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), model.EffectiveTimeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData(30)]
    public void TimeoutSeconds_RoundTrips(int? timeoutSeconds)
    {
        var model = new LockConfigurationModel
        {
            TimeoutSeconds = timeoutSeconds
        };

        Assert.Equal(timeoutSeconds, model.TimeoutSeconds);
    }
}
