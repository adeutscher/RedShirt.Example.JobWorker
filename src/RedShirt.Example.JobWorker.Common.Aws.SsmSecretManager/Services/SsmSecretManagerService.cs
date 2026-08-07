using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;
using System.Text.RegularExpressions;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services;

internal sealed partial class SsmSecretManagerService(
    IAmazonSimpleSystemsManagement ssm,
    ISsmRetryWrapperService retryWrapperService) : ISecretManagerService
{
    /// <summary>
    ///     AWS GetParameters allows at most 10 names per request.
    /// </summary>
    private const int MaxNamesPerRequest = 10;
    
    /// <summary>
    ///     Regular expression for AWS Systems Manager Parameter Store hierarchical paths.
    ///     Paths must start with /, use only a-zA-Z0-9_.- in each segment, contain at most 15
    ///     hierarchy levels, and be at most 2048 characters.
    ///     Source: https://docs.aws.amazon.com/systems-manager/latest/userguide/sysman-paramstore-su-create.html
    /// </summary>
    [GeneratedRegex(@"^(?=.{1,2048}$)(/[a-zA-Z0-9_.-]+){1,15}$")]
    private static partial Regex ValidKeyRegex();

    private static void ThrowIfInvalidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !ValidKeyRegex().IsMatch(key))
        {
            throw new WorkerSecretManagerException($"Invalid secret path: {key}")
                {CouldBeTransient = false, IsHandled = false, CouldBeExternallySolvable = false};
        }
    }

    public async Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidKey(key);
        
        var response = await retryWrapperService.RunAsync(ct =>
            ssm.GetParameterAsync(new GetParameterRequest
            {
                Name = key,
                WithDecryption = true
            }, ct), cancellationToken);

        return response.Parameter.Value;
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys,
        CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            ThrowIfInvalidKey(key);
        }
        
        var result = new Dictionary<string, string>();
        var remaining = keys.Distinct().ToList();

        while (remaining.Count > 0)
        {
            var batch = remaining.Take(MaxNamesPerRequest).ToList();
            remaining = remaining.Skip(MaxNamesPerRequest).ToList();

            var response = await retryWrapperService.RunAsync(ct =>
                ssm.GetParametersAsync(new GetParametersRequest
                {
                    Names = batch,
                    WithDecryption = true
                }, ct), cancellationToken);

            foreach (var parameter in response.Parameters)
            {
                result[parameter.Name] = parameter.Value;
            }
        }

        return result;
    }
}