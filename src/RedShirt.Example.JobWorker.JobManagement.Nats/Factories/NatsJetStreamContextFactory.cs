using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Factories;

internal interface INatsJetStreamContextFactory
{
    Task<NatsConnectionBundle> CreateConnectionAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default);
}

internal class NatsJetStreamContextFactory(
    INatsCredentialSource natsCredentialSource,
    IOptions<NatsJetStreamContextFactory.ConfigurationModel> options) : INatsJetStreamContextFactory
{
    public async Task<NatsConnectionBundle> CreateConnectionAsync(bool forceNewSecretManagerPull = false,
        CancellationToken cancellationToken = default)
    {
        var credentials = await natsCredentialSource.GetCredentialsAsync(forceNewSecretManagerPull, cancellationToken);

        var natsOpts = NatsOpts.Default with
        {
            AuthOpts = new NatsAuthOpts
            {
                Username = credentials.User,
                Password = credentials.Password
            },
            Url = options.Value.Url
        };

        var connection = new NatsConnection(natsOpts);
        return new NatsConnectionBundle(new NatsJSContext(connection));
    }

    public sealed class ConfigurationModel
    {
        public required string Url { get; init; }
    }
}