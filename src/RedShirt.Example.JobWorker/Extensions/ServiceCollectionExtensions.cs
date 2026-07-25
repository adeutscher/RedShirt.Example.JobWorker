using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Logic.Extensions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Extensions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        services = services
            .AddCoreJobManagement(configuration)
            .AddCoreLogic(configuration);

        /*
         * Template note:
         *      When adapting this template you'll want to pick one message source
         *      and prune away the other ones.
         */
        var useKinesisRaw = configuration.GetValue("UseKinesis", "0");
        var useActiveMqRaw = configuration.GetValue("UseActiveMq", "0");
        var useAzureQueueStorageRaw = configuration.GetValue("UseAzureQueueStorage", "0");
        var useAzureServiceBusRaw = configuration.GetValue("UseAzureServiceBus", "0");
        var useNatsRaw = configuration.GetValue("UseNats", "0");
        var useRabbitMqRaw = configuration.GetValue("UseRabbitMq", "0");

        if (int.TryParse(useNatsRaw, out var useNats) && useNats == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddSecretManagerSsm(configuration)
                .AddNatsJobManagement(configuration);
        }
        else if (int.TryParse(useAzureQueueStorageRaw, out var useAzureQueueStorage) && useAzureQueueStorage == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddSecretManagerAzureKeyVault(configuration)
                .AddAzureQueueStorageJobManagement(configuration);
        }
        else if (int.TryParse(useAzureServiceBusRaw, out var useAzureServiceBus) && useAzureServiceBus == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddSecretManagerAzureKeyVault(configuration)
                .AddAzureServiceBusJobManagement(configuration);
        }
        else if (int.TryParse(useRabbitMqRaw, out var useRabbitMq) && useRabbitMq == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddSecretManagerSsm(configuration)
                .AddRabbitMqJobManagement(configuration);
        }
        else if (int.TryParse(useActiveMqRaw, out var useActiveMq) && useActiveMq == 1)
        {
            services = services
                .AddSecretManagerCore(configuration)
                .AddSecretManagerSsm(configuration)
                .AddActiveMqJobManagement(configuration);
        }
        else if (int.TryParse(useKinesisRaw, out var useKinesis) && useKinesis == 1)
        {
            services = services
                .AddKinesisJobManagement(configuration);
        }
        else
        {
            services = services
                .AddSqsJobManagement(configuration);
        }

        return services;
    }
}