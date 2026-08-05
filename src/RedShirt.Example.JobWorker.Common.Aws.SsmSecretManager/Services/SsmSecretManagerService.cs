using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services;

internal sealed class SsmSecretManagerService(
    IAmazonSimpleSystemsManagement ssm,
    ISsmRetryWrapperService retryWrapperService) : ISecretManagerService
{
    /// <summary>
    ///     AWS GetParameters allows at most 10 names per request.
    /// </summary>
    private const int MaxNamesPerRequest = 10;

    public async Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
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