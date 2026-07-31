using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGooglePubSubJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            // Required
            .AddSingleton<IJobSource, GooglePubSubJobSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>()
            // Supporting
            .Configure<GooglePubSubConfigurationModel>(configuration.GetSection("JobSource:GooglePubSub"))
            .AddSingleton<IPubSubSubscriberClientFactory, PubSubSubscriberClientFactory>()
            .AddSingleton<IPubSubSubscriberClientSource, PubSubSubscriberClientSource>()
            .AddSingleton<IGooglePubSubExceptionArbiterService, GooglePubSubExceptionArbiterService>()
            .AddSingleton<IGooglePubSubRetryWrapperService, GooglePubSubRetryWrapperService>()
            .AddSingleton<IGooglePubSubMessageSource, GooglePubSubMessageSource>()
            .AddSingleton<IGooglePubSubBodyStringRetriever, GooglePubSubBodyStringRetriever>();
    }
}