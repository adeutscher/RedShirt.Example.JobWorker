using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    private static IConfigurationRoot CreateConfiguration(string? subscribe = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["JobSource:RabbitMq:QueueName"] = "jobs"
        };

        if (subscribe is not null)
        {
            values["JobSource:RabbitMq:Subscribe"] = subscribe;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Theory]
    [InlineData(null, typeof(RabbitMqJobSource))]
    [InlineData("false", typeof(RabbitMqJobSource))]
    [InlineData("true", typeof(RabbitMqSubscribeJobSource))]
    public void AddRabbitMqJobManagement_RegistersJobSourceFromSubscribeFlag(string? subscribe,
        Type expectedJobSourceType)
    {
        var services = new ServiceCollection()
            .AddRabbitMqJobManagement(CreateConfiguration(subscribe));

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == expectedJobSourceType
                                       && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddRabbitMqJobManagement_RegistersSharedSupportingServices()
    {
        var services = new ServiceCollection()
            .AddRabbitMqJobManagement(CreateConfiguration());

        Assert.Contains(services, d => d.ServiceType == typeof(IJobFailureHandler)
                                       && d.ImplementationType == typeof(NoReactionFailureHandler)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqConnectionCacheSource)
                                       && d.ImplementationType == typeof(RabbitMqConnectionCacheSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqChannelRetryWrapper)
                                       && d.ImplementationType == typeof(RabbitMqChannelRetryWrapper));
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqRetryWrapperService)
                                       && d.ImplementationType == typeof(RabbitMqRetryWrapperService));
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqExceptionArbiterService)
                                       && d.ImplementationType == typeof(RabbitMqExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqSubscribeExceptionArbiter)
                                       && d.ImplementationType == typeof(RabbitMqSubscribeExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IRabbitMqSubscribeConfigurationService));
    }
}