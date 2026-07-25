using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

/// <summary>
///     In-memory cache for secrets retrieved from an <see cref="ISecretManagerService" />.
/// </summary>
public interface ISecretManagerCacheService
{
    /// <summary>
    ///     Gets a single secret by key, returning a cached value when available and still valid.
    /// </summary>
    /// <param name="key">
    ///     The secret key to resolve.
    /// </param>
    /// <param name="expiration">
    ///     Optional absolute lifetime for a newly fetched value.
    ///     When <see langword="null" />, the entry does not expire on its own.
    /// </param>
    /// <param name="force">
    ///     When <see langword="true" />, bypasses a usable cache entry and refreshes from the secret manager,
    ///     unless the configured force cooldown has not yet elapsed since the last fetch.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation before the underlying secret manager is called.
    /// </param>
    /// <returns>
    ///     The secret value for <paramref name="key" />.
    /// </returns>
    Task<string> GetSecretAsync(string key,
        TimeSpan? expiration = null,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets multiple secrets by key, serving cached hits and fetching only the keys that still need a refresh.
    ///     Duplicate keys in <paramref name="keys" /> are collapsed before fetching.
    /// </summary>
    /// <param name="keys">
    ///     The secret keys to resolve.
    /// </param>
    /// <param name="expiration">
    ///     Optional absolute lifetime applied to each newly fetched value.
    ///     When <see langword="null" />, those entries do not expire on their own.
    /// </param>
    /// <param name="force">
    ///     When <see langword="true" />, bypasses usable cache entries and refreshes from the secret manager,
    ///     unless the configured force cooldown has not yet elapsed since each entry's last fetch.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation before the underlying secret manager is called.
    /// </param>
    /// <returns>
    ///     A dictionary of secret key to value for the requested keys that were resolved.
    /// </returns>
    Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        TimeSpan? expiration = null,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Centralized in-memory cache for secrets
/// </summary>
internal class SecretManagerCacheService(
    ISecretManagerService secretManagerService,
    IOptions<SecretManagerCacheService.ConfigurationModel> options) : ISecretManagerCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    ///     Returns a cached value when it is still valid for the request.
    ///     A <paramref name="force" /> refresh is ignored while the entry is within the configured force cooldown.
    /// </summary>
    private bool TryGetUsableCache(string key, bool force, out string value)
    {
        if (!_cache.TryGetValue(key, out var entry) || entry.IsExpired)
        {
            if (entry is {IsExpired: true})
            {
                _cache.TryRemove(key, out _);
            }

            value = null!;
            return false;
        }

        if (!force || entry.IsWithinForceCooldown(options.Value.EffectiveForceCooldownSeconds))
        {
            value = entry.Value;
            return true;
        }

        value = null!;
        return false;
    }

    private void SetCache(string key, string value, TimeSpan? expiration)
    {
        DateTimeOffset? absoluteExpiration = expiration.HasValue
            ? DateTimeOffset.UtcNow.Add(expiration.Value)
            : null;

        _cache[key] = new CacheEntry(value, absoluteExpiration, DateTimeOffset.UtcNow);
    }

    public async Task<string> GetSecretAsync(string key,
        TimeSpan? expiration = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetUsableCache(key, force, out var cached))
        {
            return cached;
        }

        var value = await secretManagerService.GetSecretAsync(key, cancellationToken);
        SetCache(key, value, expiration);
        return value;
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        TimeSpan? expiration = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<string, string>();
        var keysToFetch = new List<string>();

        foreach (var key in keys.Distinct())
        {
            if (TryGetUsableCache(key, force, out var cached))
            {
                result[key] = cached;
            }
            else
            {
                keysToFetch.Add(key);
            }
        }

        if (keysToFetch.Count == 0)
        {
            // Skip requesting secrets if there are no remaining secrets to cache
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fetched = await secretManagerService.GetSecretsAsync(keysToFetch, cancellationToken);
        foreach (var (key, value) in fetched)
        {
            SetCache(key, value, expiration);
            result[key] = value;
        }

        return result;
    }

    private sealed class CacheEntry(string value, DateTimeOffset? absoluteExpiration, DateTimeOffset fetchedAt)
    {
        private DateTimeOffset? AbsoluteExpiration { get; } = absoluteExpiration;
        private DateTimeOffset FetchedAt { get; } = fetchedAt;
        public string Value { get; } = value;

        public bool IsExpired =>
            AbsoluteExpiration is { } expiresAt && DateTimeOffset.UtcNow >= expiresAt;

        public bool IsWithinForceCooldown(int cooldownSeconds)
        {
            if (cooldownSeconds <= 0)
            {
                return false;
            }

            return DateTimeOffset.UtcNow < FetchedAt.AddSeconds(cooldownSeconds);
        }
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Minimum seconds that must elapse after a fetch before <c>force: true</c> will hit the secret manager again.
        ///     This is a safety measure against an overzealous client spamming the underlying secret source.
        /// </summary>
        public required int ForceCooldownSeconds { get; init; }

        /// <summary>
        ///     Minimum seconds that must elapse after a fetch before <c>force: true</c> will hit the secret manager again.
        /// </summary>
        public int EffectiveForceCooldownSeconds => Math.Max(1, ForceCooldownSeconds);
    }
}