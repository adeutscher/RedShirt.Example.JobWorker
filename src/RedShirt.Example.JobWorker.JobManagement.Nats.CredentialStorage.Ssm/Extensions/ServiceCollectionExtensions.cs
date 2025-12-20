using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.CredentialStorage.Ssm.Services;
using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.CredentialStorage.Ssm.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNatsCredentialsSsmStorage(this IServiceCollection services,
        IConfigurationRoot configurationRoot)
    {
        return services
            // Required
            .AddSingleton<INatsCredentialSource, NatsCredentialSourceViaSsm>()
            // Supporting
            .Configure<NatsCredentialSourceViaSsm.ConfigurationModel>(
                configurationRoot.GetSection("JobSource:NATS"))
            .AddAwsServiceWithLocalSupport<IAmazonSimpleSystemsManagement>();
    }
}