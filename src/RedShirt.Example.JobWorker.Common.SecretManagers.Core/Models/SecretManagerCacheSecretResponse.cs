namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Models;

/// <summary>
///     Result of resolving a single secret through <see cref="Services.ISecretManagerCacheService" />.
/// </summary>
public sealed class SecretManagerCacheSecretResponse
{
    /// <summary>
    ///     The secret value for the requested key.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    ///     When <see langword="true" />, the underlying <see cref="Services.ISecretManagerService" />
    ///     was queried to produce this result; when <see langword="false" />, the value came from cache.
    /// </summary>
    public required bool QueriedSecretManager { get; init; }
}