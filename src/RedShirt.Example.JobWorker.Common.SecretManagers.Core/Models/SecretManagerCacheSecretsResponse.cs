namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;

/// <summary>
///     Result of resolving multiple secrets through <see cref="Services.ISecretManagerCacheService" />.
/// </summary>
public sealed class SecretManagerCacheSecretsResponse
{
    /// <summary>
    ///     Secret key to value for the requested keys that were resolved.
    /// </summary>
    public required Dictionary<string, string> Values { get; init; }

    /// <summary>
    ///     When <see langword="true" />, the underlying <see cref="Services.ISecretManagerService" />
    ///     was queried for at least one key; when <see langword="false" />, every value came from cache.
    /// </summary>
    public required bool QueriedSecretManager { get; init; }
}