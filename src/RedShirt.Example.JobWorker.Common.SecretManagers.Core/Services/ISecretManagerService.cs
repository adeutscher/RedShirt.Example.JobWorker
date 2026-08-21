using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

/// <summary>
///     Retrieves secrets from a configured secret-manager backend
///     (for example AWS SSM Parameter Store, Azure Key Vault, or Docker secret files).
/// </summary>
/// <remarks>
///     Implementations throw <see cref="WorkerSecretManagerException" /> for classified failures
///     such as invalid keys, missing secrets, or provider errors after any internal retries.
///     Classification flags on that exception
///     (<see cref="WorkerSecretManagerException.IsHandled" />,
///     <see cref="WorkerSecretManagerException.CouldBeTransient" />,
///     <see cref="WorkerSecretManagerException.CouldBeExternallySolvable" />)
///     guide outer resilience handling.
/// </remarks>
public interface ISecretManagerService
{
    /// <summary>
    ///     Gets a single secret value by key.
    /// </summary>
    /// <param name="key">
    ///     The secret key or path to resolve. Accepted formats are backend-specific.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation.
    /// </param>
    /// <returns>
    ///     The secret value for <paramref name="key" />.
    /// </returns>
    /// <exception cref="WorkerSecretManagerException">
    ///     Thrown when the key is invalid, the secret cannot be resolved, or the backend reports a classified failure.
    /// </exception>
    Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets multiple secret values by key.
    ///     Duplicate keys are typically collapsed before fetching.
    /// </summary>
    /// <param name="keys">
    ///     The secret keys or paths to resolve. Accepted formats are backend-specific.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation.
    /// </param>
    /// <returns>
    ///     A map of requested keys to secret values.
    /// </returns>
    /// <exception cref="WorkerSecretManagerException">
    ///     Thrown when a key is invalid, a secret cannot be resolved, or the backend reports a classified failure.
    /// </exception>
    Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys, CancellationToken cancellationToken = default);
}