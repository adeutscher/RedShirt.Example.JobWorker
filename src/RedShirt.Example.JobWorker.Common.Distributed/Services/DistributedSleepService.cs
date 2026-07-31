namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

/// <summary>
///     Abstraction of sleeping for Distributed services.
///     Copied from Core's <c>ISleepService</c> to avoid a circular project dependency for such a lightweight service.
///     Its use prevents certain unit tests from greatly increasing test times in Docker builds, CI/CD pipelines, or a developer's manual tests.
/// </summary>
public interface IDistributedSleepService
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

internal class DistributedSleepService : IDistributedSleepService
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
