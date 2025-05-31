using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.FileDownload.Core.Services;
using RedShirt.Example.JobWorker.FileDownload.S3.Extensions;

namespace RedShirt.Example.JobWorker.FileDownload.S3.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionTests
{
    [Fact]
    public void Test1()
    {
        var provider = new ServiceCollection()
            .AddFileDownloadS3(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var environment = new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseKinesis"] = "1"
        };

        TestUtilities.WrapEnvironment(environment, () =>
            Assert.NotNull(provider.GetRequiredService<IFileDownloadService>()));
    }
}