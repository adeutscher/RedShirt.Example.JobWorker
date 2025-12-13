using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Logic.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.ConfigurationStorage.Ssm.Extensions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        services = services
            .AddCoreLogic(configuration);

        /*
         * Template note:
         *      When adapting this template you'll want to pick one message source
         *      and prune away the other ones.
         */
        var useKinesisRaw = configuration.GetValue("UseKinesis", "0");
        var useRabbitMqRaw = configuration.GetValue("UseRabbitMq", "0");

        if (int.TryParse(useRabbitMqRaw, out var useRabbitMq) && useRabbitMq >= 1)
        {
            services = services
                .AddRabbitMqJobManagement(configuration)
                .AddRabbitMqConfigurationSsmStorage(configuration);
        }
        else if (int.TryParse(useKinesisRaw, out var useKinesis) && useKinesis >= 1)
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