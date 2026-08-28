using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureServiceBusJobManagement_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:AzureServiceBus:QueueName"] = "jobs"
            })
            .Build();

        services.AddAzureServiceBusJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(AzureServiceBusJobSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IJobFailureHandler)
                                       && d.ImplementationType == typeof(NoReactionFailureHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureServiceBusExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureServiceBusDetailedExceptionArbiter));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureServiceBusRetryWrapperService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAzureServiceBusClientRetryWrapper));
    }

    [Fact]
    public void AddAzureServiceBusJobManagement_WhenSubscribeTrue_RegistersSubscribeJobSource()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:AzureServiceBus:QueueName"] = "jobs",
                ["JobSource:AzureServiceBus:Subscribe"] = "true"
            })
            .Build();

        services.AddAzureServiceBusJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(AzureServiceBusSubscribeJobSource));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IJobSource)
                                             && d.ImplementationType == typeof(AzureServiceBusJobSource));
    }
}