using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Extensions;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Extensions;

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

        // Note: S3 needs a special carve-out for AmazonS3Config.ForcePathStyle that is not needed here.

        Console.WriteLine($"Using AWS service URL: {url}");

        return services.AddAWSService<TService>(new AWSOptions
        {
            DefaultClientConfig =
            {
                ServiceURL = url
            }
        });
    }

    public static IServiceCollection AddSqsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCommonJobManagement(configuration)
            .AddAwsServiceWithLocalSupport<IAmazonSQS>()
            .AddSingleton<IJobSource, SqsJobSource>()
            .Configure<SqsJobSource.ConfigurationModel>(configuration.GetSection("JobSource:SQS"))
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>();
    }
}