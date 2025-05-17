using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using RedShirt.Example.JobWorker.FileDownload.S3.Services;
using System.Text;

namespace RedShirt.Example.JobWorker.FileDownload.S3.UnitTests.Tests.Services;

public class S3DownloadServiceTests
{
    [Fact]
    public async Task TestDownloadAsync()
    {
        const string contents = "abc";
        const string expectedMd5 = "900150983cd24fb0d6963f7d28e17f72";
        const string expectedSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
        const string expectedSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        const string expectedSha512 = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";

        var bucket = Guid.NewGuid().ToString();
        var key = Guid.NewGuid().ToString();
        
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(s => s.GetObjectAsync(It.Is<GetObjectRequest>(r => r.BucketName == bucket && r.Key == key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(contents))
            });
        
        var downloadService = new S3DownloadService(s3.Object);

        var tempPath = Path.GetTempFileName();

        try
        {
            var report = await downloadService.DownloadAsync(bucket, key, tempPath);
            Assert.True(File.Exists(tempPath));
            Assert.Equal(contents, await File.ReadAllTextAsync(tempPath));
            Assert.Equal(expectedMd5, report.Md5);
            Assert.Equal(expectedSha1, report.Sha1);
            Assert.Equal(expectedSha256, report.Sha256);
            Assert.Equal(expectedSha512, report.Sha512);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}