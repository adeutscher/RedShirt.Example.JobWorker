using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddActiveMqJobManagement_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:ActiveMq:QueueName"] = "jobs"
            })
            .Build();

        services.AddActiveMqJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(ActiveMqJobSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IJobFailureHandler)
                                       && d.ImplementationType == typeof(NoReactionFailureHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IActiveMqExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IActiveMqSubscribeExceptionArbiter));
        Assert.Contains(services, d => d.ServiceType == typeof(IActiveMqRetryWrapperService));
        Assert.Contains(services, d => d.ServiceType == typeof(IActiveMqConsumerRetryWrapper)
                                       && d.ImplementationType == typeof(ActiveMqConsumerRetryWrapper));
    }

    [Fact]
    public void AddActiveMqJobManagement_WhenSubscribeTrue_RegistersSubscribeJobSource()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:ActiveMq:QueueName"] = "jobs",
                ["JobSource:ActiveMq:Subscribe"] = "true"
            })
            .Build();

        services.AddActiveMqJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(ActiveMqSubscribeJobSource));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IJobSource)
                                             && d.ImplementationType == typeof(ActiveMqJobSource));
    }
}