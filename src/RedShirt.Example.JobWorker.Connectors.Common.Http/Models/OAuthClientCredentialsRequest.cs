namespace RedShirt.Example.JobWorker.Connectors.Common.Http.Models;

/// <summary>
///     Inputs for an OAuth 2.0 client-credentials token request against a token endpoint.
///     Client id/secret are resolved from the secret manager via the configured paths.
/// </summary>
public class OAuthClientCredentialsRequest
{
    /// <summary>
    ///     Absolute URL of the OAuth token endpoint.
    /// </summary>
    public required string TokenUrl { get; init; }

    /// <summary>
    ///     Secret-manager path for the OAuth client id.
    /// </summary>
    public required string ClientIdPath { get; init; }

    /// <summary>
    ///     Secret-manager path for the OAuth client secret.
    /// </summary>
    public required string ClientSecretPath { get; init; }

    /// <summary>
    ///     Optional form-field name used for the scope/audience-style parameter
    ///     (for example <c>scope</c> or <c>audience</c>).
    ///     When null, no scope-style field is sent.
    /// </summary>
    public required string? ScopeLabel { get; init; }

    /// <summary>
    ///     Optional value for <see cref="ScopeLabel" />.
    ///     When null (or when <see cref="ScopeLabel" /> is null), no scope-style field is sent.
    /// </summary>
    public required string? ScopeValue { get; init; }
}