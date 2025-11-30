using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Logic.Extensions;
using RedShirt.Example.JobWorker.FileDownload.S3.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Extensions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureWorker(this IServiceCollection services, IConfigurationRoot configuration)
    {
        services = services
            .AddFileDownloadS3(configuration)
            .AddCoreLogic(configuration);

        var useKinesisRaw = configuration.GetValue("UseKinesis", "0");

        if (int.TryParse(useKinesisRaw, out var useKinesis) && useKinesis >= 1)
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