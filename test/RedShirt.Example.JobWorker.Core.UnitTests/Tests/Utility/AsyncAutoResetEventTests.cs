using RedShirt.Example.JobWorker.Core.Utility;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Utility;

public class AsyncAutoResetEventTests
{
    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_InitiallySet_ReturnsTrueImmediately()
    {
        var evt = new AsyncAutoResetEvent(true);

        var sw = Stopwatch.StartNew();
        var result = await evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.True(result);
        Assert.True(sw.ElapsedMilliseconds < 250);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_InitiallySet_ConsumesSignal()
    {
        var evt = new AsyncAutoResetEvent(true);

        Assert.True(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.False(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_InitiallyUnset_ZeroTimeout_ReturnsFalse()
    {
        var evt = new AsyncAutoResetEvent();

        var result = await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_AfterSet_ReturnsTrueWithoutBlocking()
    {
        var evt = new AsyncAutoResetEvent();
        evt.Set();

        var result = await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_Set_WithNoWaiters_OnlyStoresOneSignal()
    {
        var evt = new AsyncAutoResetEvent();

        evt.Set();
        evt.Set();
        evt.Set();

        Assert.True(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.False(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_Timeout_ReturnsFalse()
    {
        var evt = new AsyncAutoResetEvent();

        var sw = Stopwatch.StartNew();
        var result = await evt.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.False(result);
        Assert.InRange(sw.ElapsedMilliseconds, 50, 500);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_UnblocksWhenSet()
    {
        var evt = new AsyncAutoResetEvent();

        var waitTask = evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        evt.Set();

        Assert.True(await waitTask);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_Set_WakesOnlyOneWaiter()
    {
        var evt = new AsyncAutoResetEvent();

        var first = evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        evt.Set();

        var completed = await Task.WhenAny(first, second);
        Assert.True(await completed);

        // Give the other waiter a moment; it must still be blocked
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var stillWaiting = completed == first ? second : first;
        Assert.False(stillWaiting.IsCompleted);

        evt.Set();
        Assert.True(await stillWaiting);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_Set_ReleasesWaitersInFifoOrder()
    {
        var evt = new AsyncAutoResetEvent();
        var releaseOrder = new ConcurrentQueue<int>();

        // Start waits sequentially so queue order is deterministic
        var waiters = new Task[3];
        for (var i = 0; i < waiters.Length; i++)
        {
            var index = i;
            waiters[i] = AwaitAndRecordAsync(evt, index, releaseOrder);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < waiters.Length; i++)
        {
            evt.Set();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(waiters);

        Assert.Equal([0, 1, 2], releaseOrder.ToArray());
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_MultipleSets_ReleaseMatchingNumberOfWaiters()
    {
        var evt = new AsyncAutoResetEvent();

        var waiters = Enumerable.Range(0, 4)
            .Select(_ => evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ToArray();

        await Task.Delay(50, TestContext.Current.CancellationToken);

        evt.Set();
        evt.Set();

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(2, waiters.Count(t => t.IsCompletedSuccessfully));

        evt.Set();
        evt.Set();

        var results = await Task.WhenAll(waiters);
        Assert.All(results, Assert.True);
    }

    private static async Task AwaitAndRecordAsync(
        AsyncAutoResetEvent evt,
        int index,
        ConcurrentQueue<int> releaseOrder)
    {
        Assert.True(await evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        releaseOrder.Enqueue(index);
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_CancelledToken_Throws()
    {
        var evt = new AsyncAutoResetEvent();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact(Timeout = 1000)]
    public async Task Test_WaitAsync_CancelledToken_WhenAlreadySet_ThrowsAndConsumesSignal()
    {
        var evt = new AsyncAutoResetEvent(true);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token));

        // Signal was cleared on the fast path before throwing
        Assert.False(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_CancelledWhileWaiting_Throws()
    {
        var evt = new AsyncAutoResetEvent();
        using var cts = new CancellationTokenSource();

        var waitTask = evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitTask);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_CancelOneWaiter_DoesNotAffectOthers()
    {
        var evt = new AsyncAutoResetEvent();
        using var cts = new CancellationTokenSource();

        var cancelledWait = evt.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        var survivingWait = evt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledWait);

        Assert.False(survivingWait.IsCompleted);
        evt.Set();
        Assert.True(await survivingWait);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_InfiniteTimeout_UnblocksWhenSet()
    {
        var evt = new AsyncAutoResetEvent();

        var waitTask = evt.WaitAsync(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        evt.Set();

        Assert.True(await waitTask);
    }

    [Fact(Timeout = 2000)]
    public async Task Test_WaitAsync_TimeoutRemovesWaiter_SoLaterSetIsStored()
    {
        var evt = new AsyncAutoResetEvent();

        Assert.False(await evt.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));

        evt.Set();

        Assert.True(await evt.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }
}
