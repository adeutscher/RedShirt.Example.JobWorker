using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Extensions;
using RedShirt.Example.JobWorker.Common.Distributed.Extensions;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Extensions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Extensions;
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
    private static JobSourceKind ResolveJobSourceKind(IConfiguration configuration)
    {
        // Declare candidates that can be toggled on (first match wins, defaults to SQS if no match) 
        (string Key, JobSourceKind Kind)[] candidates =
        [
            ("UseNats", JobSourceKind.Nats),
            ("UseRedisStreams", JobSourceKind.RedisStreams),
            ("UseAzureQueueStorage", JobSourceKind.AzureQueueStorage),
            ("UseAzureServiceBus", JobSourceKind.AzureServiceBus),
            ("UseGooglePubSub", JobSourceKind.GooglePubSub),
            ("UseRabbitMq", JobSourceKind.RabbitMq),
            ("UseActiveMq", JobSourceKind.ActiveMq),
            ("UseKinesis", JobSourceKind.Kinesis),
            ("UseKafka", JobSourceKind.Kafka),
            ("UsePulsar", JobSourceKind.Pulsar)
        ];

        foreach (var (key, kind) in candidates)
        {
            if (int.TryParse(configuration.GetValue(key, "0"), out var value) && value == 1)
            {
                return kind;
            }
        }

        return JobSourceKind.Sqs;
    }

    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        services = services
            // A secret manager is required for idempotency support
            .AddSecretManagerCore(configuration)
            // This general template assumes SSM by default as it's easier to local test with.
            // Azure Service Bus and Azure Queue Storage implementations below will override this with Key Vault
            // If you wish to switch default handling to a different secret manager,
            //   then you should change out SSM for one of the below line.
            //.AddSecretManagerDocker(configuration)
            //.AddSecretManagerAzureKeyVault(configuration)
            .AddSecretManagerSsm(configuration)
            // Distributed Services
            .AddDistributedServices(configuration)
            // Core job handling
            .AddCoreJobManagement(configuration)
            // Implementation logic
            .AddCoreLogic(configuration)
            // Bar connector (stand-in for an OAuth API client; see docs/bar-connector.md)
            .AddBarConnector(configuration);

        /*
         * Template note:
         *      When adapting this template, it is assumed that you will want to pick one message source and prune away the rest.
         */
        // ReSharper disable once ConvertSwitchStatementToSwitchExpression
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (ResolveJobSourceKind(configuration))
        {
            case JobSourceKind.Nats:
                services = services
                    .AddNatsJobManagement(configuration);
                break;
            case JobSourceKind.RedisStreams:
                services = services
                    .AddRedisStreamsJobManagement(configuration);
                break;
            case JobSourceKind.AzureQueueStorage:
                services = services
                    .AddSecretManagerAzureKeyVault(configuration)
                    .AddAzureQueueStorageJobManagement(configuration);
                break;
            case JobSourceKind.AzureServiceBus:
                services = services
                    .AddSecretManagerAzureKeyVault(configuration)
                    .AddAzureServiceBusJobManagement(configuration);
                break;
            case JobSourceKind.GooglePubSub:
                services = services
                    .AddGooglePubSubJobManagement(configuration);
                break;
            case JobSourceKind.RabbitMq:
                services = services
                    .AddSecretManagerCore(configuration)
                    .AddRabbitMqJobManagement(configuration);
                break;
            case JobSourceKind.ActiveMq:
                services = services
                    .AddSecretManagerCore(configuration)
                    .AddActiveMqJobManagement(configuration);
                break;
            case JobSourceKind.Kinesis:
                services = services
                    .AddSecretManagerCore(configuration)
                    .AddKinesisJobManagement(configuration);
                break;
            case JobSourceKind.Kafka:
                services = services
                    .AddKafkaJobManagement(configuration)
                    .AddKafkaSqsFailureHandling(configuration);
                break;
            case JobSourceKind.Pulsar:
                services = services
                    .AddPulsarJobManagement(configuration);
                break;
            default:
                services = services
                    .AddSqsJobManagement(configuration);
                break;
        }

        return services;
    }

    private enum JobSourceKind
    {
        Nats,
        RedisStreams,
        AzureQueueStorage,
        AzureServiceBus,
        GooglePubSub,
        RabbitMq,
        ActiveMq,
        Kinesis,
        Kafka,
        Pulsar,
        Sqs
    }
}