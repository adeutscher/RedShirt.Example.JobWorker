using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;

namespace RedShirt.Example.JobWorker.Common.Aws.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the shared AWS exception arbiter.
    /// </summary>
    public static IServiceCollection AddAwsResiliency(this IServiceCollection services)
    {
        return services
            .AddSingleton<IAwsExceptionArbiterService, AwsExceptionArbiterService>();
    }

    public static IServiceCollection AddAwsServiceWithLocalSupport<TService>(this IServiceCollection services)
        where TService : IAmazonService
    {
        var url = Environment.GetEnvironmentVariable("AWS_SERVICE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return services
                .AddAWSService<TService>();
        }

        /*
         * Note: Not needed here, but S3 needs a special carve-out for AmazonS3Config.ForcePathStyle
         *           in order to avoid DNS troubles
         */

        Console.WriteLine($"Using AWS service URL: {url}");

        return services.AddAWSService<TService>(new AWSOptions
        {
            DefaultClientConfig =
            {
                ServiceURL = url
            }
        });
    }
}