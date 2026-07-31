using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisStreamsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSingleton<IJobSource, RedisStreamsJobSource>()
            .Configure<RedisStreamsJobSource.ConfigurationModel>(configuration.GetSection("JobSource:RedisStreams"))
            .AddSingleton<IRedisStreamsExceptionArbiterService, RedisStreamsExceptionArbiterService>()
            .AddSingleton<IRedisStreamsRetryWrapperService, RedisStreamsRetryWrapperService>()
            .AddSingleton<IRedisStreamBodyRetriever, RedisStreamBodyRetriever>();
    }
}
