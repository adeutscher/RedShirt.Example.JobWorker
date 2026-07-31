namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

/// <summary>
///     Abstraction over a remote string cache. Implementations may throw
///     <see cref="Exceptions.CacheException" /> (or derived types) on failure.
/// </summary>
public interface IRemoteCacheService
{
    /// <summary>
    ///     Retrieve a string under the specified key.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The cached string, or <c>null</c> if the key is missing or empty.</returns>
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Set a string under the specified key with the given expiry.
    ///     If the value provided is a null or empty string, then instead delete the key out of the cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <param name="expiry"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task SetStringAsync(string key, string? value, TimeSpan expiry, CancellationToken cancellationToken = default);
}