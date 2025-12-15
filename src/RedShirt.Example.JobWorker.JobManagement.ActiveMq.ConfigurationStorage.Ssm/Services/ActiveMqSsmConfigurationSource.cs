using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.ConfigurationStorage.Ssm.Services;

internal class ActiveMqSsmConfigurationSource(
    IAmazonSimpleSystemsManagement ssm,
    IOptions<ActiveMqSsmConfigurationSource.ConfigurationModel> options) : IActiveMqServerConfigurationSource
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

    public async Task<ActiveMqServerConfigurationModel> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var user = string.IsNullOrWhiteSpace(_user) ? await GetFreshUserAsync(cancellationToken) : _user;
        var password = string.IsNullOrWhiteSpace(_password)
            ? await GetFreshPasswordAsync(cancellationToken)
            : _password;

        return new ActiveMqServerConfigurationModel
        {
            BrokerUri = options.Value.BrokerUri,
            User = user,
            Password = password
        };
    }

    public sealed class ConfigurationModel
    {
        public required string BrokerUri { get; init; }
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}