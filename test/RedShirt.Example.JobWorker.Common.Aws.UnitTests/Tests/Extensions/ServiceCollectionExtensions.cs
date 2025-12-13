using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;

namespace RedShirt.Example.JobWorker.Common.Aws.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensions
{
    [Fact]
    public void Test_Configure_NoServiceUrl()
    {
        var environment = new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = string.Empty,
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar"
        };

        TestUtilities.WrapEnvironment(environment, () =>
        {
            var provider = new ServiceCollection()
                .AddAwsServiceWithLocalSupport<IAmazonSimpleSystemsManagement>()
                .BuildServiceProvider();
            Assert.NotNull(provider);
        });
    }

    [Fact]
    public void Test_Configure_WithServiceUrl()
    {
        var environment = new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://1234",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar"
        };

        TestUtilities.WrapEnvironment(environment, () =>
        {
            var provider = new ServiceCollection()
                .AddAwsServiceWithLocalSupport<IAmazonSimpleSystemsManagement>()
                .BuildServiceProvider();
            Assert.NotNull(provider);
        });
    }
}