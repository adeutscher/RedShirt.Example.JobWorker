using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Common.Distributed.Extensions;

/// <summary>
///     JSON object helpers over <see cref="IRemoteCacheService" /> string get/set operations.
/// </summary>
public static class RemoteCacheExtensions
{
    /// <summary>
    ///     Reads a JSON-serialized object from the remote cache.
    /// </summary>
    /// <typeparam name="T">The reference type to deserialize.</typeparam>
    /// <param name="remoteCacheService">The remote string cache.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Token used to cancel the underlying cache read.</param>
    /// <returns>
    ///     The deserialized instance, or <c>null</c> when the key is missing, the stored value is blank,
    ///     or the stored value is not valid JSON for <typeparamref name="T" />.
    /// </returns>
    /// <remarks>
    ///     Invalid JSON is treated as a cache miss (<c>null</c>) rather than throwing
    ///     <see cref="JsonException" />.
    /// </remarks>
    // ReSharper disable once ConvertToExtensionBlock
    public static async Task<T?> GetObjectAsync<T>(this IRemoteCacheService remoteCacheService, string key,
        CancellationToken cancellationToken = default) where T : class
    {
        var value = await remoteCacheService.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch (JsonException)
        {
            // pass
            return null;
        }
    }

    /// <summary>
    ///     Serializes <paramref name="value" /> as JSON and writes it to the remote cache with the given expiry.
    /// </summary>
    /// <typeparam name="T">The reference type to serialize.</typeparam>
    /// <param name="remoteCacheService">The remote string cache.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The object to store.</param>
    /// <param name="expiry">How long the entry should live in the cache.</param>
    /// <param name="cancellationToken">Token used to cancel the underlying cache write.</param>
    /// <returns>A task that completes when the value has been stored.</returns>
    public static Task SetObjectAsync<T>(this IRemoteCacheService remoteCacheService, string key, T value,
        TimeSpan expiry,
        CancellationToken cancellationToken = default) where T : class
    {
        return remoteCacheService.SetStringAsync(key, JsonSerializer.Serialize(value), expiry, cancellationToken);
    }
}