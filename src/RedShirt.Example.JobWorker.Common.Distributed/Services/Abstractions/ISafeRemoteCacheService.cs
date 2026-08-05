using RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

/// <summary>
///     Safe version of IRemoteCacheService that will filter out non-critical exceptions.
///     Intended for cases where caching is nice to have but not a deal-breaker in a pinch.
/// </summary>
public interface ISafeRemoteCacheService
{
    /// <summary>
    ///     Attempt to retrieve a string under the specified key.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<SafeDistributedGetOperationResponse<string?>> GetStringAsync(string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Attempt to set a string. If the value provided is a null or empty string, then instead attempt to delete the key
    ///     out of the cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <param name="expiry"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<SafeDistributedOperationResponse> SetStringAsync(string key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken = default);
}