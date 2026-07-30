using RedShirt.Example.JobWorker.Core.Configuration;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Configuration;

public class IdempotencyConfigurationModelTests
{
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 11)]
    [InlineData(100, 100)]
    public void EffectiveResultCacheDurationSeconds_ClampsToMinimumOfTen(int resultCacheDurationSeconds,
        int expectedEffective)
    {
        var model = new IdempotencyConfigurationModel
        {
            Enabled = true,
            ResultCacheDurationSeconds = resultCacheDurationSeconds,
            MonitorIntervalSeconds = 3,
            IdempotencyIdsCanRepeat = false
        };

        Assert.Equal(expectedEffective, model.EffectiveResultCacheDurationSeconds);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(60, 60)]
    public void EffectiveMonitorIntervalSeconds_ClampsToMinimumOfThree(int monitorIntervalSeconds,
        int expectedEffective)
    {
        var model = new IdempotencyConfigurationModel
        {
            Enabled = true,
            ResultCacheDurationSeconds = 10,
            MonitorIntervalSeconds = monitorIntervalSeconds,
            IdempotencyIdsCanRepeat = false
        };

        Assert.Equal(expectedEffective, model.EffectiveMonitorIntervalSeconds);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RequiredFlags_RoundTrip(bool enabled, bool idempotencyIdsCanRepeat)
    {
        var model = new IdempotencyConfigurationModel
        {
            Enabled = enabled,
            ResultCacheDurationSeconds = 10,
            MonitorIntervalSeconds = 3,
            IdempotencyIdsCanRepeat = idempotencyIdsCanRepeat
        };

        Assert.Equal(enabled, model.Enabled);
        Assert.Equal(idempotencyIdsCanRepeat, model.IdempotencyIdsCanRepeat);
    }
}
