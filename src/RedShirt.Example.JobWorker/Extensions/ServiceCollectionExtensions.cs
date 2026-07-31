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
using RedShirt.Example.JobWorker.JobManagement.Kafka.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        services = services
            // A secret manager is required for idempotency support
            .AddSecretManagerCore(configuration)
            // This general template assumes SSM by default as it's easier to local test with.
            // Azure Service Bus and Azure Queue Storage implementations below will override this with Key Vault
            // If you wish to switch default handling to a different secret manager, then you should change the below line. 
            .AddSecretManagerSsm(configuration)
            // Distributed Services
            .AddDistributedServices(configuration)
            // Core job handling
            .AddCoreJobManagement(configuration)
            // Implementation logic
            .AddCoreLogic(configuration);

        /*
         * Template note:
         *      When adapting this template, you will want to pick one message source and prune away the rest.
         */
        var useKinesisRaw = configuration.GetValue("UseKinesis", "0");
        var useKafkaRaw = configuration.GetValue("UseKafka", "0");
        var usePulsarRaw = configuration.GetValue("UsePulsar", "0");
        var useActiveMqRaw = configuration.GetValue("UseActiveMq", "0");
        var useAzureQueueStorageRaw = configuration.GetValue("UseAzureQueueStorage", "0");
        var useAzureServiceBusRaw = configuration.GetValue("UseAzureServiceBus", "0");
        var useNatsRaw = configuration.GetValue("UseNats", "0");
        var useRabbitMqRaw = configuration.GetValue("UseRabbitMq", "0");

        if (int.TryParse(useNatsRaw, out var useNats) && useNats == 1)
        {
            services = services
                .AddNatsJobManagement(configuration);
        }
        else if (int.TryParse(useAzureQueueStorageRaw, out var useAzureQueueStorage) && useAzureQueueStorage == 1)
        {
            services = services
                .AddSecretManagerAzureKeyVault(configuration)
                .AddAzureQueueStorageJobManagement(configuration);
        }
        else if (int.TryParse(useAzureServiceBusRaw, out var useAzureServiceBus) && useAzureServiceBus == 1)
        {
            services = services
                .AddSecretManagerAzureKeyVault(configuration)
                .AddAzureServiceBusJobManagement(configuration);
        }
        else if (int.TryParse(useRabbitMqRaw, out var useRabbitMq) && useRabbitMq == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddRabbitMqJobManagement(configuration);
        }
        else if (int.TryParse(useActiveMqRaw, out var useActiveMq) && useActiveMq == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddActiveMqJobManagement(configuration);
        }
        else if (int.TryParse(useKinesisRaw, out var useKinesis) && useKinesis == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddKinesisJobManagement(configuration);
        }
        else if (int.TryParse(useKafkaRaw, out var useKafka) && useKafka == 1)
        {
            services = services
                .AddKafkaJobManagement(configuration)
                .AddKafkaSqsFailureHandling(configuration);
        }
        else if (int.TryParse(usePulsarRaw, out var usePulsar) && usePulsar == 1)
        {
            services = services
                .AddPulsarJobManagement(configuration);
        }
        else
        {
            services = services
                .AddSqsJobManagement(configuration);
        }

        return services;
    }
}