using RedShirt.Example.JobWorker.Core.Utility;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Utility;

public class AsyncManualResetEventTests
{
    [Fact(Timeout = 2000)]
    public async Task Test_Pulse_SetThenReset_WakesCurrentWaiters_BlocksNewOnes()
    {
        var evt = new AsyncManualResetEvent();

        var pendingWaiters = Enumerable.Range(0, 4)
            .Select(_ => evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ToArray();

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.All(pendingWaiters, t => Assert.False(t.IsCompleted));

        // Same pulse pattern used by JobRepository.LoadAsync
        evt.Set();
        evt.Reset();

        var results = await Task.WhenAll(pendingWaiters);
        Assert.All(results, Assert.True);

        var lateResult = await evt.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.False(lateResult);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_Reset_AfterSet_BlocksSubsequentWaiters()
    {
        var evt = new AsyncManualResetEvent(true);
        evt.Reset();

        var result = await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_Reset_WhenUnset_IsSafe()
    {
        var evt = new AsyncManualResetEvent();

        evt.Reset();
        evt.Reset();

        Assert.False(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_Set_WhenAlreadySet_RemainsSignaled()
    {
        var evt = new AsyncManualResetEvent(true);

        evt.Set();
        evt.Set();

        Assert.True(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_AfterSet_ReturnsTrueWithoutBlocking()
    {
        var evt = new AsyncManualResetEvent();
        evt.Set();

        var result = await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_CancellationTokenOnly_UnblocksWhenSet()
    {
        var evt = new AsyncManualResetEvent();

        var waitTask = evt.WaitAsync(TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        evt.Set();

        Assert.True(await waitTask);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_CancelledToken_Throws()
    {
        var evt = new AsyncManualResetEvent();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_CancelledToken_WhenAlreadySet_Throws()
    {
        var evt = new AsyncManualResetEvent(true);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_CancelledWhileWaiting_Throws()
    {
        var evt = new AsyncManualResetEvent();
        using var cts = new CancellationTokenSource();

        var waitTask = evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitTask);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_InfiniteTimeout_UnblocksWhenSet()
    {
        var evt = new AsyncManualResetEvent();

        var waitTask = evt.WaitAsync(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        evt.Set();

        Assert.True(await waitTask);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_InitiallySet_ReturnsTrueImmediately()
    {
        var evt = new AsyncManualResetEvent(true);

        var sw = Stopwatch.StartNew();
        var result = await evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.True(result);
        Assert.True(sw.ElapsedMilliseconds < 250);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_InitiallyUnset_ZeroTimeout_ReturnsFalse()
    {
        var evt = new AsyncManualResetEvent();

        var result = await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_Set_WakesAllWaiters()
    {
        var evt = new AsyncManualResetEvent();

        var waiters = Enumerable.Range(0, 8)
            .Select(_ => evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ToArray();

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.All(waiters, t => Assert.False(t.IsCompleted));

        evt.Set();

        var results = await Task.WhenAll(waiters);
        Assert.All(results, Assert.True);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_StickySet_AllowsRepeatedWaits()
    {
        var evt = new AsyncManualResetEvent();
        evt.Set();

        Assert.True(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.True(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.True(await evt.WaitAsync(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_Timeout_ReturnsFalse()
    {
        var evt = new AsyncManualResetEvent();

        var sw = Stopwatch.StartNew();
        var result = await evt.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.False(result);
        Assert.InRange(sw.ElapsedMilliseconds, 50, 500);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_UnblocksWhenSet()
    {
        var evt = new AsyncManualResetEvent();

        var waitTask = evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        evt.Set();

        Assert.True(await waitTask);
    }
}