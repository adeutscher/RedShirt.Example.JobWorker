using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.FileDownload.S3.Services;

namespace RedShirt.Example.JobWorker.FileDownload.S3.UnitTests.Tests.Services;

public class S3BucketSourceTests
{
    [Fact]
    public void Test1()
    {
        var bucket = Guid.NewGuid().ToString();

        var options = new S3BucketSource.ConfigurationModel
        {
            BucketName = bucket
        };

        var source = new S3BucketSource(Options.Create(options));

        Assert.Equal(bucket, source.Bucket);
    }
}