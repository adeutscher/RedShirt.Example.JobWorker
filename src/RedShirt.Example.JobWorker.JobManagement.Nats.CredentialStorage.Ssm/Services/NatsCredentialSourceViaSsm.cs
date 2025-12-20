using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.CredentialStorage.Ssm.Services;

internal class NatsCredentialSourceViaSsm(
    IAmazonSimpleSystemsManagement ssm,
    IOptions<NatsCredentialSourceViaSsm.ConfigurationModel> options) : INatsCredentialSource
{
    private string? _password;
    private string? _user;

    private async Task<string> GetFreshPasswordAsync(CancellationToken cancellationToken = default)
    {
        var response = await ssm.GetParameterAsync(new GetParameterRequest
        {
            WithDecryption = true,
            Name = options.Value.PasswordPath
        }, cancellationToken);

        _password = response.Parameter.Value;
        return response.Parameter.Value;
    }

    private async Task<string> GetFreshUserAsync(CancellationToken cancellationToken = default)
    {
        var response = await ssm.GetParameterAsync(new GetParameterRequest
        {
            WithDecryption = true,
            Name = options.Value.UserPath
        }, cancellationToken);

        _user = response.Parameter.Value;
        return response.Parameter.Value;
    }

    public async Task<NatsCredentialModel> GetCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        var user = string.IsNullOrWhiteSpace(_user) ? await GetFreshUserAsync(cancellationToken) : _user;
        var password = string.IsNullOrWhiteSpace(_password)
            ? await GetFreshPasswordAsync(cancellationToken)
            : _password;

        return new NatsCredentialModel
        {
            User = user,
            Password = password
        };
    }

    public sealed class ConfigurationModel
    {
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}