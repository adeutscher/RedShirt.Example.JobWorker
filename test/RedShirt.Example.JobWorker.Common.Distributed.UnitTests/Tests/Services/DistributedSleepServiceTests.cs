using RedShirt.Example.JobWorker.Common.Distributed.Services;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class DistributedSleepServiceTests
{
    [Theory(Timeout = 5000)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task DelayAsync_WaitsApproximatelyRequestedDuration(int delayTimeMs)
    {
        var sleepService = new DistributedSleepService();

        var sw = Stopwatch.StartNew();
        await sleepService.DelayAsync(TimeSpan.FromMilliseconds(delayTimeMs), TestContext.Current.CancellationToken);
        sw.Stop();

        var lowerBound = delayTimeMs - 250;
        var upperBound = delayTimeMs + 250;
        Assert.InRange(sw.ElapsedMilliseconds, lowerBound, upperBound);
    }
}
