using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureQueueStorageJobManagement_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:AzureQueueStorage:Uri"] = "https://example.queue.core.windows.net/jobs"
            })
            .Build();

        services.AddAzureQueueStorageJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(AzureQueueStorageJobSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IJobFailureHandler)
                                       && d.ImplementationType == typeof(NoReactionFailureHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureQueueStorageExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureQueueStorageRetryWrapperService));
    }
}
