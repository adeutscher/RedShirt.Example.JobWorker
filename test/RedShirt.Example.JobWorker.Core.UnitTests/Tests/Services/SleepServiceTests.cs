using RedShirt.Example.JobWorker.Core.Services;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class SleepServiceTests
{
    [Theory(Timeout = 5000)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task Test_Delay(int delayTimeMs)
    {
        var sleepService = new SleepService();

        var sw = Stopwatch.StartNew();
        await sleepService.DelayAsync(TimeSpan.FromMilliseconds(delayTimeMs), TestContext.Current.CancellationToken);
        sw.Stop();

        var lowerBound = delayTimeMs - 250;
        var upperBound = delayTimeMs + 250;
        Assert.InRange(sw.ElapsedMilliseconds, lowerBound, upperBound);
    }
}