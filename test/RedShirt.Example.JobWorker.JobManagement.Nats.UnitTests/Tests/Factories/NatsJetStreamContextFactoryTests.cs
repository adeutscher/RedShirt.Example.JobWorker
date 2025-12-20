using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Models;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Factories;

public class NatsJetStreamContextFactoryTests
{
    [Fact]
    public async Task Test_CreateNatsJSContextAsync()
    {
        var username = Guid.NewGuid().ToString();
        var password = Guid.NewGuid().ToString();

        var credentialsSource = new Mock<INatsCredentialSource>();
        credentialsSource
            .Setup(s => s.GetCredentialsAsync(TestContext.Current.CancellationToken))
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

        var context = await factory.CreateNatsJSContextAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(context);

        Assert.Equal(url, context.Connection.Opts.Url);
        Assert.Equal(username, context.Connection.Opts.AuthOpts.Username);
        Assert.Equal(password, context.Connection.Opts.AuthOpts.Password);

        credentialsSource.Verify(s => s.GetCredentialsAsync(TestContext.Current.CancellationToken), Times.Once);
    }
}