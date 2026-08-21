using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.ExecutionState;

public class ExecutionEndArbiterTests
{
    private static ExecutionEndArbiter CreateArbiter()
    {
        return new ExecutionEndArbiter(NullLogger<ExecutionEndArbiter>.Instance);
    }

    [Fact]
    public void AddCallback_AfterStop_InvokesImmediatelyWithStoredException()
    {
        using var arbiter = CreateArbiter();
        var exception = new InvalidOperationException("already stopped");
        Exception? received = null;

        arbiter.Stop(exception);
        arbiter.AddOnStopCallback(e => received = e);

        Assert.Same(exception, received);
    }

    [Fact]
    public void AddCallback_WhenNull_ThrowsArgumentNullException()
    {
        using var arbiter = CreateArbiter();

        Assert.Throws<ArgumentNullException>(() => arbiter.AddOnStopCallback(null!));
    }

    [Fact]
    public async Task Dispose_DoesNotStopRunningOrReleaseWaiters()
    {
        var arbiter = CreateArbiter();
        var waitTask = arbiter.WaitForFinishedAsync(TestContext.Current.CancellationToken);

        ((IDisposable) arbiter).Dispose();

        Assert.True(arbiter.IsRunning);
        Assert.True(arbiter.ShouldKeepRunning());
        Assert.False(waitTask.IsCompleted);

        arbiter.Stop();
        await waitTask;
    }

    [Fact]
    public async Task HandleSigTerm_StopsRunningInvokesCallbacksAndReleasesWaiters()
    {
        using var arbiter = CreateArbiter();
        var received = new Exception("sentinel");
        var waitTask = arbiter.WaitForFinishedAsync(TestContext.Current.CancellationToken);

        arbiter.AddOnStopCallback(e => received = e);
        arbiter.HandleSigTerm(null, EventArgs.Empty);

        Assert.False(arbiter.IsRunning);
        Assert.False(arbiter.ShouldKeepRunning());
        Assert.Null(received);
        await waitTask;
    }

    [Fact]
    public void HandleSigTerm_WhenAlreadyStopped_DoesNotInvokeCallbacksAgain()
    {
        using var arbiter = CreateArbiter();
        var invocations = 0;
        arbiter.AddOnStopCallback(_ => invocations++);

        arbiter.Stop();
        arbiter.HandleSigTerm(null, EventArgs.Empty);

        Assert.Equal(1, invocations);
        Assert.False(arbiter.ShouldKeepRunning());
    }

    [Fact]
    public void ShouldKeepRunning_WhenConstructed_IsTrue()
    {
        using var arbiter = CreateArbiter();

        Assert.True(arbiter.IsRunning);
        Assert.True(arbiter.ShouldKeepRunning());
    }

    [Fact]
    public void Stop_InvokesRegisteredCallbacksOnceWithException()
    {
        using var arbiter = CreateArbiter();
        var exceptions = new List<Exception?>();
        var exception = new InvalidOperationException("stop");

        arbiter.AddOnStopCallback(exceptions.Add);
        arbiter.AddOnStopCallback(exceptions.Add);

        arbiter.Stop(exception);
        arbiter.Stop(new Exception("ignored"));

        Assert.Equal(2, exceptions.Count);
        Assert.All(exceptions, received => Assert.Same(exception, received));
        Assert.False(arbiter.ShouldKeepRunning());
    }

    [Fact]
    public async Task Stop_IsThreadSafeAndRunsOnce()
    {
        using var arbiter = CreateArbiter();
        var invocations = 0;
        arbiter.AddOnStopCallback(_ => Interlocked.Increment(ref invocations));

        await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() => arbiter.Stop(new Exception()))));

        Assert.Equal(1, invocations);
        Assert.False(arbiter.ShouldKeepRunning());
    }

    [Fact]
    public async Task WaitForStopAsync_CompletesWhenStopIsCalled()
    {
        using var arbiter = CreateArbiter();

        var waitTask = arbiter.WaitForFinishedAsync(TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        arbiter.Stop();

        await waitTask;
        Assert.False(arbiter.ShouldKeepRunning());
    }

    [Fact]
    public async Task WaitForStopAsync_WhenAlreadyStopped_CompletesImmediately()
    {
        using var arbiter = CreateArbiter();
        arbiter.Stop();

        await arbiter.WaitForFinishedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForStopAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        using var arbiter = CreateArbiter();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            arbiter.WaitForFinishedAsync(cts.Token));
        Assert.True(arbiter.ShouldKeepRunning());
    }
}