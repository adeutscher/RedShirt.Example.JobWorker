using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Factories;

public interface INatsJetStreamContextFactory
{
    Task<INatsJSContext> CreateNatsJSContextAsync(CancellationToken cancellationToken = default);
}

internal class NatsJetStreamContextFactory(
    INatsCredentialSource natsCredentialSource,
    IOptions<NatsJetStreamContextFactory.ConfigurationModel> options) : INatsJetStreamContextFactory
{
    public async Task<INatsJSContext> CreateNatsJSContextAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await natsCredentialSource.GetCredentialsAsync(cancellationToken);

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
        return new NatsJSContext(connection);
    }

    public class ConfigurationModel
    {
        public required string Url { get; init; }
    }
}