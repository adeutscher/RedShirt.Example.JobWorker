using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.ConfigurationStorage.Ssm.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.ConfigurationStorage.Ssm.UnitTests.Tests.Services;

public class RabbitMqSsmConfigurationSourceTests
{
    [Fact]
    public async Task Test_Get()
    {
        // Declare
        var paramName = Guid.NewGuid().ToString();
        var paramPassword = Guid.NewGuid().ToString();

        var valueName = Guid.NewGuid().ToString();
        var valuePassword = Guid.NewGuid().ToString();
        var valueHostname = Guid.NewGuid().ToString();
        var valueVirtualHost = Guid.NewGuid().ToString();

        // Setup
        var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);

        var x = ssm.Setup(s =>
                s.GetParameterAsync(It.Is<GetParameterRequest>(r => r.Name == paramName && r.WithDecryption == true),
                    TestContext.Current.CancellationToken))
            .ReturnsAsync(new GetParameterResponse
            {
                Parameter = new Parameter
                {
                    Value = valueName
                }
            });

        ssm.Setup(s =>
                s.GetParameterAsync(
                    It.Is<GetParameterRequest>(r => r.Name == paramPassword && r.WithDecryption == true),
                    TestContext.Current.CancellationToken))
            .ReturnsAsync(new GetParameterResponse
            {
                Parameter = new Parameter
                {
                    Value = valuePassword
                }
            });

        var configuration = new RabbitMqSsmConfigurationSource.ConfigurationModel
        {
            Hostname = valueHostname,
            VHost = valueVirtualHost,
            UserPath = paramName,
            PasswordPath = paramPassword
        };

        // Execute
        var source = new RabbitMqSsmConfigurationSource(ssm.Object, Options.Create(configuration));

        // Assert
        var output = await source.GetConfigurationAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(output);
        Assert.Equal(valueName, output.User);
        Assert.Equal(valuePassword, output.Password);
        Assert.Equal(valueHostname, output.Hostname);
        Assert.Equal(valueVirtualHost, output.VirtualHost);

        Assert.Equal(2, ssm.Invocations.Count);

        // Calling again
        output = await source.GetConfigurationAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(output);
        Assert.Equal(valueName, output.User);
        Assert.Equal(valuePassword, output.Password);
        Assert.Equal(valueHostname, output.Hostname);
        Assert.Equal(valueVirtualHost, output.VirtualHost);

        // Invocations should STILL be at just 2
        Assert.Equal(2, ssm.Invocations.Count);
    }
}