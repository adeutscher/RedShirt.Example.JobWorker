using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Factories;

public class NatsJetStreamContextFactoryTests
{
    [Fact]
    public async Task Test_CreateConnectionAsync()
    {
        var username = Guid.NewGuid().ToString();
        var password = Guid.NewGuid().ToString();

        var credentialsSource = new Mock<INatsCredentialSource>(MockBehavior.Strict);
        credentialsSource
            .Setup(s => s.GetCredentialsAsync(false, TestContext.Current.CancellationToken))
            .ReturnsAsync(new NatsCredentialModel
            {
                User = username,
                Password = password
            });

        var url = Guid.NewGuid().ToString();

        var options = new NatsJetStreamContextFactory.ConfigurationModel
        {
            Url = url
        };

        var factory = new NatsJetStreamContextFactory(credentialsSource.Object, Options.Create(options));

        var bundle = await factory.CreateConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(bundle);

        Assert.Equal(url, bundle.Connection.Opts.Url);
        Assert.Equal(username, bundle.Connection.Opts.AuthOpts.Username);
        Assert.Equal(password, bundle.Connection.Opts.AuthOpts.Password);

        credentialsSource.Verify(s => s.GetCredentialsAsync(false, TestContext.Current.CancellationToken), Times.Once);
        credentialsSource.VerifyNoOtherCalls();
    }
}