using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers AWS resiliency, the SQS client (with local endpoint support), and SQS arbiter/retry.
    /// </summary>
    public static IServiceCollection AddSqs(this IServiceCollection services)
    {
        return services
            .AddAwsResiliency()
            .AddAwsServiceWithLocalSupport<IAmazonSQS>()
            .AddSingleton<ISqsExceptionArbiterService, SqsExceptionArbiterService>()
            .AddSingleton<ISqsRetryWrapperService, SqsRetryWrapperService>();
    }
}