namespace RedShirt.Example.JobWorker.Common.Services;

/// <summary>
///     Abstraction of sleeping and timed waits.
///     This amount is not strictly necessary for the operation of the program.
///     However, its use does prevent certain unit tests from greatly increasing test times in Docker builds, CI/CD
///     pipelines, or just a developer's manual tests.
/// </summary>
public interface ISleepService
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Waits for <paramref name="task" /> to complete, or throws <see cref="TimeoutException" />
    ///     if <paramref name="timeout" /> elapses first. Wraps
    ///     <see cref="Task.WaitAsync(System.TimeSpan, System.Threading.CancellationToken)" />.
    /// </summary>
    Task WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Waits for <paramref name="task" /> to complete and returns its result, or throws
    ///     <see cref="TimeoutException" /> if <paramref name="timeout" /> elapses first. Wraps
    ///     <see cref="Task.WaitAsync{TResult}(System.TimeSpan, System.Threading.CancellationToken)" />.
    /// </summary>
    Task<TResult> WaitAsync<TResult>(Task<TResult> task, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

internal sealed class SleepService : ISleepService
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return Task.Delay(delay, cancellationToken);
    }

    public Task WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return task.WaitAsync(timeout, cancellationToken);
    }

    public Task<TResult> WaitAsync<TResult>(Task<TResult> task, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return task.WaitAsync(timeout, cancellationToken);
    }
}
