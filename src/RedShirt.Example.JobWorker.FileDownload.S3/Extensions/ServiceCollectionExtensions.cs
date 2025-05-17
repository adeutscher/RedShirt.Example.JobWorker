using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.FileDownload.Core.Services;
using RedShirt.Example.JobWorker.FileDownload.S3.Services;

namespace RedShirt.Example.JobWorker.FileDownload.S3.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileDownloadS3(this IServiceCollection serviceCollection,
        IConfigurationRoot configurationRoot)
    {
        return serviceCollection
            .AddAwsServiceWithLocalSupport<IAmazonS3>()
            .AddSingleton<IS3BucketSource, S3BucketSource>()
            .Configure<S3BucketSource.ConfigurationModel>(configurationRoot.GetSection("Download"))
            .AddSingleton<IFileDownloadService, S3FileDownloadService>()
            .AddSingleton<IS3DownloadService, S3DownloadService>();
    }
}