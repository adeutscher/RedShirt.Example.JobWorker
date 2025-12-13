using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace RedShirt.Example.JobWorker.Common.Aws.Extensions;

public static class ServiceCollectionExtensions
{
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