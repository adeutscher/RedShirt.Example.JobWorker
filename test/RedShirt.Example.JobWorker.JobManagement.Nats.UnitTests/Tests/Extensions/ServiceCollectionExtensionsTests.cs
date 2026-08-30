using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Factories;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData("jobs-stream", 5)]
    [InlineData("other-stream", 0)]
    public void AddNatsJobManagement_ConfiguresStreamAndWaitTime(string streamName, int waitTimeSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:NATS:StreamName"] = streamName,
                ["JobSource:NATS:WaitTimeSeconds"] = waitTimeSeconds.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddNatsJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var stream = provider.GetRequiredService<IOptions<NatsStreamConfigurationModel>>().Value;
        Assert.Equal(streamName, stream.StreamName);

        var messageSource = provider.GetRequiredService<IOptions<NatsMessageSource.ConfigurationModel>>().Value;
        Assert.Equal(waitTimeSeconds, messageSource.WaitTimeSeconds);
        Assert.Equal(Math.Max(waitTimeSeconds, 0), messageSource.EffectiveWaitTimeSeconds);
    }

    [Fact]
    public void AddNatsJobManagement_RegistersExpectedServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:NATS:StreamName"] = "jobs",
                ["JobSource:NATS:WaitTimeSeconds"] = "5",
                ["JobSource:NATS:Url"] = "nats://localhost:4222"
            })
            .Build();

        var services = new ServiceCollection()
            .AddNatsJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(NatsJobSource)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IJobFailureHandler)
                                       && d.ImplementationType == typeof(NoReactionFailureHandler)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsMessageSource)
                                       && d.ImplementationType == typeof(NatsMessageSource)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsConsumerSource)
                                       && d.ImplementationType == typeof(NatsConsumerSource)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsConnectionCacheSource)
                                       && d.ImplementationType == typeof(NatsConnectionCacheSource)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsJetStreamContextFactory)
                                       && d.ImplementationType == typeof(NatsJetStreamContextFactory)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsCredentialSource)
                                       && d.ImplementationType == typeof(NatsCredentialSource)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsExceptionArbiterService)
                                       && d.ImplementationType == typeof(NatsExceptionArbiterService)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsRetryWrapperService)
                                       && d.ImplementationType == typeof(NatsRetryWrapperService)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(INatsConnectionRetryWrapper)
                                       && d.ImplementationType == typeof(NatsConnectionRetryWrapper)
                                       && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddNatsJobManagement_WhenSubscribeTrue_RegistersSubscribeJobSource()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:NATS:StreamName"] = "jobs",
                ["JobSource:NATS:Subscribe"] = "true"
            })
            .Build();

        var services = new ServiceCollection()
            .AddNatsJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(NatsSubscribeJobSource));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IJobSource)
                                             && d.ImplementationType == typeof(NatsJobSource));
    }
}