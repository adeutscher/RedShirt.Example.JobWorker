using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Distributed.Extensions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Logic.Extensions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        
        return services;
    }
}