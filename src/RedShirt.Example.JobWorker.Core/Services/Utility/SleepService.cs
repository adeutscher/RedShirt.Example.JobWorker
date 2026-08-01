namespace RedShirt.Example.JobWorker.Core.Services.Utility;

/// <summary>
///     Abstraction of sleeping.
///     This amount is not strictly necessary for the operation of the program.
///     However, its use does prevent certain unit tests from greatly increasing test times in Docker builds, CI/CD
///     pipelines, or
///     just a developer's manual tests.
/// </summary>
public interface ISleepService
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

internal sealed class SleepService : ISleepService
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return Task.Delay(delay, cancellationToken);
    }
}