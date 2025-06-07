using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Common.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCommonJobManagement(configuration)
            .AddAwsServiceWithLocalSupport<IAmazonSQS>()
            .AddSingleton<IJobSource, SqsJobSource>()
            .AddSingleton<ISqsMessageSource, SqsMessageSource>()
            .Configure<SqsConfigurationModel>(configuration.GetSection("JobSource:SQS"))
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>();
    }
}