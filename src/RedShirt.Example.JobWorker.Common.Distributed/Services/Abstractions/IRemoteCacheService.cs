namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

public interface IRemoteCacheService
{
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);
    Task SetStringAsync(string? key, string? value, TimeSpan expiry, CancellationToken cancellationToken = default);
}