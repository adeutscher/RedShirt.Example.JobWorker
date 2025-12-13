using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Models;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.ConfigurationStorage.Ssm.Services;

public class RabbitMqSsmConfigurationSource(
    IAmazonSimpleSystemsManagement ssm,
    IOptions<RabbitMqSsmConfigurationSource.ConfigurationModel> options) : IRabbitMqServerConfigurationSource
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

    public async Task<RabbitMqServerConfigurationModel> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var user = string.IsNullOrWhiteSpace(_user) ? await GetFreshUserAsync(cancellationToken) : _user;
        var password = string.IsNullOrWhiteSpace(_password)
            ? await GetFreshPasswordAsync(cancellationToken)
            : _password;

        return new RabbitMqServerConfigurationModel
        {
            Hostname = options.Value.Hostname,
            VirtualHost = options.Value.VHost,
            User = user,
            Password = password
        };
    }

    public sealed class ConfigurationModel
    {
        public required string Hostname { get; init; }
        public required string VHost { get; init; }
        public required string UserPath { get; init; }
        public required string PasswordPath { get; init; }
    }
}