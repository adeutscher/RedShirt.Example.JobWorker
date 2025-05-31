using Moq;
using RedShirt.Example.JobWorker.FileDownload.Core.Models;
using RedShirt.Example.JobWorker.FileDownload.S3.Services;

namespace RedShirt.Example.JobWorker.FileDownload.S3.UnitTests.Tests.Services;

public class S3FileDownloadServiceTests
{
    [Fact]
    public async Task TestDownloadAsync()
    {
        var bucketSource = new Mock<IS3BucketSource>();
        var s3 = new Mock<IS3DownloadService>();

        var bucket = Guid.NewGuid().ToString();
        bucketSource.Setup(s => s.Bucket).Returns(bucket);

        var readPath = Guid.NewGuid().ToString();
        var writePath = Guid.NewGuid().ToString();

        var downloadService = new S3FileDownloadService(s3.Object, bucketSource.Object);

        var report = new FileDownloadReport
        {
            FilePath = null!,
            FileSize = 0,
            Md5 = null!,
            Sha1 = null!,
            Sha256 = null!,
            Sha512 = null!
        };

        s3.Setup(s => s.DownloadAsync(bucket, readPath, writePath, It.IsAny<CancellationToken>())).ReturnsAsync(report);

        var result = await downloadService.DownloadAsync(readPath, writePath);

        s3.Verify(s => s.DownloadAsync(bucket, readPath, writePath, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(s3.Invocations);
        Assert.Same(report, result);
    }
}