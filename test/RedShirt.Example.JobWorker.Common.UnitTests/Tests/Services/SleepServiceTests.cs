using RedShirt.Example.JobWorker.Common.Services;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Common.UnitTests.Tests.Services;

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

    [Fact(Timeout = 5000)]
    public async Task WaitAsync_WhenGenericTaskCompletesWithinTimeout_ReturnsResult()
    {
        var sleepService = new SleepService();
        var task = Task.Run(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            return "ok";
        });

        var result = await sleepService.WaitAsync(task, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
    }

    [Fact(Timeout = 5000)]
    public async Task WaitAsync_WhenGenericTaskExceedsTimeout_ThrowsTimeoutException()
    {
        var sleepService = new SleepService();
        var task = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
            return "late";
        });

        await Assert.ThrowsAsync<TimeoutException>(() =>
            sleepService.WaitAsync(task, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task WaitAsync_WhenTaskCompletesWithinTimeout_Completes()
    {
        var sleepService = new SleepService();
        var task = Task.Delay(50, TestContext.Current.CancellationToken);

        await sleepService.WaitAsync(task, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact(Timeout = 5000)]
    public async Task WaitAsync_WhenTaskExceedsTimeout_ThrowsTimeoutException()
    {
        var sleepService = new SleepService();
        // Ignore the wait token so the underlying delay outlasts the WaitAsync timeout.
        var task = Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            sleepService.WaitAsync(task, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
    }
}
