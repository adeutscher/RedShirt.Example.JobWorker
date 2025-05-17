using Microsoft.Extensions.Options;

namespace RedShirt.Example.JobWorker.FileDownload.S3.Services;

internal interface IS3BucketSource
{
    string Bucket { get; }
}

internal class S3BucketSource(IOptions<S3BucketSource.ConfigurationModel> options) : IS3BucketSource
{
    public string Bucket { get; } = options.Value.BucketName;

    public class ConfigurationModel
    {
        public required string BucketName { get; init; }
    }
}