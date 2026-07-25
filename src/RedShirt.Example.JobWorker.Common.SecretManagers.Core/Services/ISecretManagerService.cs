namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

public interface ISecretManagerService
{
    Task<string> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetSecretsAsync(List<string> keys, CancellationToken cancellationToken = default);
}