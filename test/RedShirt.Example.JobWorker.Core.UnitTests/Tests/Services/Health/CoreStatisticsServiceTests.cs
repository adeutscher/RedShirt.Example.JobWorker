using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Health;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Health;

public class CoreStatisticsServiceTests
{
    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public AdjustableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void Advance(TimeSpan delta)
        {
            _utcNow += delta;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private static CoreStatisticsService CreateService(
        TimeProvider timeProvider,
        int recentWindowSeconds = 60,
        int bucketDurationSeconds = 10)
    {
        return new CoreStatisticsService(
            Options.Create(new CoreStatisticsService.ConfigurationModel
            {
                RecentWindowSeconds = recentWindowSeconds,
                RecentBucketDurationSeconds = bucketDurationSeconds
            }),
            timeProvider);
    }

    [Fact]
    public void GetStatistics_WhenEmpty_ReturnsZeroedLifetimeAndRecent()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = CreateService(time);

        var stats = service.GetStatistics();

        Assert.Equal(TimeSpan.FromSeconds(60), stats.RecentWindow);
        Assert.Equal(0, stats.Lifetime.Totals.Received);
        Assert.Equal(0, stats.Recent.Totals.Received);
        Assert.Equal(TimeSpan.Zero, stats.Lifetime.SuccessfulTimings.Average);
        Assert.Equal(TimeSpan.Zero, stats.Recent.SuccessfulTimings.Average);
    }

    [Fact]
    public void RecordReceivedAndResults_UpdateLifetimeAndRecent()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = CreateService(time);

        service.RecordReceived();
        service.RecordReceived();
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromMilliseconds(100));
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromMilliseconds(300));
        service.RecordResult(CoreJobResult.Failure);
        service.RecordResult(CoreJobResult.Cancelled);
        service.RecordResult(CoreJobResult.InvalidData);

        var stats = service.GetStatistics();

        Assert.Equal(2, stats.Lifetime.Totals.Received);
        Assert.Equal(2, stats.Lifetime.Totals.Successful);
        Assert.Equal(1, stats.Lifetime.Totals.Failed);
        Assert.Equal(1, stats.Lifetime.Totals.Cancelled);
        Assert.Equal(1, stats.Lifetime.Totals.InvalidData);
        Assert.Equal(TimeSpan.FromMilliseconds(200), stats.Lifetime.SuccessfulTimings.Average);
        Assert.Equal(TimeSpan.FromMilliseconds(100), stats.Lifetime.SuccessfulTimings.Min);
        Assert.Equal(TimeSpan.FromMilliseconds(300), stats.Lifetime.SuccessfulTimings.Max);

        Assert.Equal(2, stats.Recent.Totals.Received);
        Assert.Equal(2, stats.Recent.Totals.Successful);
        Assert.Equal(1, stats.Recent.Totals.Failed);
        Assert.Equal(1, stats.Recent.Totals.Cancelled);
        Assert.Equal(1, stats.Recent.Totals.InvalidData);
        Assert.Equal(TimeSpan.FromMilliseconds(200), stats.Recent.SuccessfulTimings.Average);
    }

    [Fact]
    public void GetStatistics_RecentExcludesSamplesOutsideWindow()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        // 30s window, 10s buckets → 3 buckets
        var service = CreateService(time, recentWindowSeconds: 30, bucketDurationSeconds: 10);

        service.RecordReceived();
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromSeconds(1));
        service.RecordResult(CoreJobResult.Failure);

        time.Advance(TimeSpan.FromSeconds(40));

        service.RecordReceived();
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromSeconds(5));

        var stats = service.GetStatistics();

        Assert.Equal(2, stats.Lifetime.Totals.Received);
        Assert.Equal(2, stats.Lifetime.Totals.Successful);
        Assert.Equal(1, stats.Lifetime.Totals.Failed);

        Assert.Equal(1, stats.Recent.Totals.Received);
        Assert.Equal(1, stats.Recent.Totals.Successful);
        Assert.Equal(0, stats.Recent.Totals.Failed);
        Assert.Equal(TimeSpan.FromSeconds(5), stats.Recent.SuccessfulTimings.Average);
        Assert.Equal(TimeSpan.FromSeconds(5), stats.Recent.SuccessfulTimings.Min);
        Assert.Equal(TimeSpan.FromSeconds(5), stats.Recent.SuccessfulTimings.Max);
    }

    [Fact]
    public void GetStatistics_UptimeTracksTimeProvider()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new AdjustableTimeProvider(start);
        var service = CreateService(time);

        time.Advance(TimeSpan.FromMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(12), service.GetStatistics().Uptime);
    }
}
