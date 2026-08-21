using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Health;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Health;

public class CoreStatisticsServiceTests
{
    [Fact]
    public void GetStatistics_WhenEmpty_ReturnsZeroTotalsAndTimings()
    {
        var service = new CoreStatisticsService();

        var stats = service.GetStatistics();

        Assert.True(stats.Uptime >= TimeSpan.Zero);
        Assert.Equal(0, stats.Lifetime.Totals.Received);
        Assert.Equal(0, stats.Lifetime.Totals.Successful);
        Assert.Equal(0, stats.Lifetime.Totals.Cancelled);
        Assert.Equal(0, stats.Lifetime.Totals.Failed);
        Assert.Equal(0, stats.Lifetime.Totals.InvalidData);
        Assert.Equal(TimeSpan.Zero, stats.Lifetime.SuccessfulTimings.Average);
        Assert.Equal(TimeSpan.Zero, stats.Lifetime.SuccessfulTimings.Min);
        Assert.Equal(TimeSpan.Zero, stats.Lifetime.SuccessfulTimings.Max);
    }

    [Fact]
    public void RecordReceived_IncrementsReceivedTotal()
    {
        var service = new CoreStatisticsService();

        service.RecordReceived();
        service.RecordReceived();

        Assert.Equal(2, service.GetStatistics().Lifetime.Totals.Received);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.InvalidData)]
    public void RecordResult_IncrementsExpectedNonSuccessBucket(CoreJobResult result)
    {
        var service = new CoreStatisticsService();

        service.RecordResult(result);

        var totals = service.GetStatistics().Lifetime.Totals;
        Assert.Equal(0, totals.Successful);
        Assert.Equal(result == CoreJobResult.Failure ? 1 : 0, totals.Failed);
        Assert.Equal(result == CoreJobResult.Cancelled ? 1 : 0, totals.Cancelled);
        Assert.Equal(
            result is CoreJobResult.Empty or CoreJobResult.Parsing or CoreJobResult.InvalidData ? 1 : 0,
            totals.InvalidData);
    }

    [Fact]
    public void RecordResult_WhenSuccessWithNegativeDuration_ClampsToZeroTicks()
    {
        var service = new CoreStatisticsService();

        service.RecordResult(CoreJobResult.Success, TimeSpan.FromTicks(-10));

        var timings = service.GetStatistics().Lifetime.SuccessfulTimings;
        Assert.Equal(TimeSpan.Zero, timings.Average);
        Assert.Equal(TimeSpan.Zero, timings.Min);
        Assert.Equal(TimeSpan.Zero, timings.Max);
    }

    [Fact]
    public void RecordResult_WhenSuccess_TracksTimings()
    {
        var service = new CoreStatisticsService();

        service.RecordResult(CoreJobResult.Success, TimeSpan.FromSeconds(2));
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromSeconds(4));
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromSeconds(6));

        var stats = service.GetStatistics();
        Assert.Equal(3, stats.Lifetime.Totals.Successful);
        Assert.Equal(TimeSpan.FromSeconds(4), stats.Lifetime.SuccessfulTimings.Average);
        Assert.Equal(TimeSpan.FromSeconds(2), stats.Lifetime.SuccessfulTimings.Min);
        Assert.Equal(TimeSpan.FromSeconds(6), stats.Lifetime.SuccessfulTimings.Max);
    }

    [Fact]
    public void RecordResult_WhenUnknownEnumValue_ThrowsArgumentOutOfRangeException()
    {
        var service = new CoreStatisticsService();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.RecordResult((CoreJobResult) 999));

        Assert.Equal("result", ex.ParamName);
    }
}