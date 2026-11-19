using RedShirt.Example.JobWorker.Core.Configuration;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Configuration;

public class IdempotencyConfigurationModelTests
{
    private static IdempotencyConfigurationModel CreateWithNamespace(string? keyNamespace)
    {
        return new IdempotencyConfigurationModel
        {
            Enabled = true,
            ResultCacheDurationSeconds = 10,
            MonitorIntervalSeconds = 3,
            IdempotencyIdsCanRepeat = false,
            EnableTraceLogging = false,
            KeyNamespace = keyNamespace
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EffectiveKeyNamespace_DefaultsToApplicationName(string? keyNamespace)
    {
        Assert.Equal(AppDomain.CurrentDomain.FriendlyName, CreateWithNamespace(keyNamespace).EffectiveKeyNamespace);
    }

    [Fact]
    public void EffectiveKeyNamespace_UsesTrimmedConfiguredValue()
    {
        Assert.Equal("orders-worker", CreateWithNamespace("  orders-worker ").EffectiveKeyNamespace);
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
            IdempotencyIdsCanRepeat = false,
            EnableTraceLogging = false
        };

        Assert.Equal(expectedEffective, model.EffectiveMonitorIntervalSeconds);
    }

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
            IdempotencyIdsCanRepeat = false,
            EnableTraceLogging = false
        };

        Assert.Equal(expectedEffective, model.EffectiveResultCacheDurationSeconds);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("orders-worker", true)]
    [InlineData("orders:worker", false)]
    [InlineData(":", false)]
    public void IsValid_RejectsColonInNamespace(string? keyNamespace, bool expected)
    {
        Assert.Equal(expected, IdempotencyConfigurationModel.IsValid(CreateWithNamespace(keyNamespace)));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void RequiredFlags_RoundTrip(bool enabled, bool idempotencyIdsCanRepeat, bool enableTraceLogging)
    {
        var model = new IdempotencyConfigurationModel
        {
            Enabled = enabled,
            ResultCacheDurationSeconds = 10,
            MonitorIntervalSeconds = 3,
            IdempotencyIdsCanRepeat = idempotencyIdsCanRepeat,
            EnableTraceLogging = enableTraceLogging
        };

        Assert.Equal(enabled, model.Enabled);
        Assert.Equal(idempotencyIdsCanRepeat, model.IdempotencyIdsCanRepeat);
        Assert.Equal(enableTraceLogging, model.EnableTraceLogging);
    }
}