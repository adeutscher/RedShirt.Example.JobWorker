using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using System.Text.RegularExpressions;

namespace RedShirt.Example.JobWorker.Common.Docker.SecretManager.Services;

/// <summary>
///     Reads secrets from Docker/Compose secret files (typically under <c>/run/secrets</c>).
/// </summary>
internal sealed partial class DockerSecretManagerService(
    IOptions<DockerSecretManagerService.ConfigurationModel> options) : ISecretManagerService
{
    /// <summary>
    ///     A Docker Compose secret key name must follow standard YAML and Docker object naming conventions. It should use
    ///     alphanumeric characters, underscores (_), or hyphens (-), avoiding special symbols or spaces. Because the key
    ///     typically dictates the default filename mounted inside the container at /run/secrets/, it should ideally conform to
    ///     valid file and shell identifier rules.
    ///     This regular expression makes sure that the secret key conforms to that standard. Mostly.
    ///     Setting a ceiling of 250 stays under the Unix file name limit of 255 characters while leaving room for the
    ///     extensions that this implementation may try.
    /// </summary>
    /// <returns></returns>
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{1,250}$")]
    private static partial Regex SecretKeyRegex();

    /// <summary>
    ///     Get the path to a secret.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    /// <exception cref="WorkerSecretManagerException">Thrown when no valid key file can be found.</exception>
    private string ResolveExistingPath(string key)
    {
        var baseDirectory = Path.GetFullPath(options.Value.EffectiveDirectory); // shorthand

        foreach (var candidate in EnumerateCandidates(key, baseDirectory))
        {
            var fullCandidatePath = Path.GetFullPath(candidate);

            if (File.Exists(fullCandidatePath))
            {
                return fullCandidatePath;
            }
        }

        throw new WorkerSecretManagerException($"Secret file not found: {key}")
        {
            CouldBeTransient = false,
            IsHandled = false,
            CouldBeExternallySolvable = true
        };
    }

    /// <summary>
    ///     Clean up content by trailing file markers. Makes the assumption that the non-newline content is what matters to the
    ///     invoker.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static string TrimTrailingNewlines(string value)
    {
        return value.TrimEnd('\r', '\n');
    }

    private static IEnumerable<string> EnumerateCandidates(string key, string directory)
    {
        yield return Path.Combine(directory, key);
        /*
         * Try some common file extensions.
         *
         * Realistically, I doubt anyone's going to be going through the trouble about naming their target file the same
         * as their key but with an extension. This is largely in deference to Cursor's first crack at this implementation.
         * The original implementation took way too much inspiration from SSM paths and assumed that the same format
         * could be conveniently mapped to Docker secret paths.
         */
        yield return Path.Combine(directory, $"{key}.txt");
        yield return Path.Combine(directory, $"{key}.json");
    }

    public async Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        /* Validate */
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new WorkerSecretManagerException("Secret key is required")
            {
                CouldBeTransient = false,
                IsHandled = false,
                CouldBeExternallySolvable = false
            };
        }

        if (!SecretKeyRegex().IsMatch(key))
        {
            throw new WorkerSecretManagerException($"Invalid secret key: {key}")
            {
                CouldBeTransient = false,
                IsHandled = false,
                CouldBeExternallySolvable = false
            };
        }

        /* Resolve/Read */
        var path = ResolveExistingPath(key);
        var value = await File.ReadAllTextAsync(path, cancellationToken);
        return TrimTrailingNewlines(value);
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            result[key] = await GetSecretAsync(key, cancellationToken);
        }

        return result;
    }

    internal static bool IsUnderDirectory(string fullPath, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var path = Path.GetFullPath(fullPath);
        return path.Equals(root, StringComparison.Ordinal)
               || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Directory that contains secret files. Defaults to Docker's <c>/run/secrets</c>.
        /// </summary>
        public string? Directory { get; init; }

        public string EffectiveDirectory =>
            string.IsNullOrWhiteSpace(Directory) ? "/run/secrets" : Directory;
    }
}