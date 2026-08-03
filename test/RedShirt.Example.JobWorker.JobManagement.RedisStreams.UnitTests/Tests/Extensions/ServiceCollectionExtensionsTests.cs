using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRedisStreamsJobManagement_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:RedisStreams:StreamName"] = "jobs",
                ["JobSource:RedisStreams:GroupName"] = "job-worker"
            })
            .Build();

        services.AddRedisStreamsJobManagement(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobSource)
                                       && d.ImplementationType == typeof(RedisStreamsJobSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IJobFailureHandler)
                                       && d.ImplementationType == typeof(NoReactionFailureHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IRedisStreamsExceptionArbiterService));
        Assert.Contains(services, d => d.ServiceType == typeof(IRedisStreamsRetryWrapperService));
    }
}