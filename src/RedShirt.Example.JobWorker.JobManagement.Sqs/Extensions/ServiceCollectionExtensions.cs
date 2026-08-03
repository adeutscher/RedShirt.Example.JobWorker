using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Extensions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqsJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddSqs()
            .AddSingleton<ISqsJobSourceExceptionArbiterService, SqsJobSourceExceptionArbiterService>()
            .AddSingleton<ISqsJobSourceRetryWrapperService, SqsJobSourceRetryWrapperService>()
            .AddSingleton<IJobSource, SqsJobSource>()
            .AddSingleton<ISqsMessageSource, SqsMessageSource>()
            .AddSingleton<ISqsPoisonMessagesHandler, SqsPoisonMessagesHandler>()
            .Configure<SqsConfigurationModel>(configuration.GetSection("JobSource:SQS"))
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>();
    }
}