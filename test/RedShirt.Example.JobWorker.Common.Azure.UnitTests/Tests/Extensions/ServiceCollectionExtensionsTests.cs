using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Azure.Extensions;
using RedShirt.Example.JobWorker.Common.Azure.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCommonAzureServices_RegistersArbiterAndPackagerAsSingletons()
    {
        var services = new ServiceCollection()
            .AddCommonAzureServices();

        using var provider = services.BuildServiceProvider();

        var arbiter1 = provider.GetRequiredService<IAzureExceptionArbiterService>();
        var arbiter2 = provider.GetRequiredService<IAzureExceptionArbiterService>();

        Assert.IsType<AzureExceptionArbiterService>(arbiter1);
        Assert.Same(arbiter1, arbiter2);
    }

    [Fact]
    public void AddCommonAzureServices_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddCommonAzureServices();

        Assert.Same(services, returned);
    }
}
