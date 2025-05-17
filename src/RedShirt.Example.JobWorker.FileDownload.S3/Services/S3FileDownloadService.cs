using RedShirt.Example.JobWorker.FileDownload.Core.Models;
using RedShirt.Example.JobWorker.FileDownload.Core.Services;

namespace RedShirt.Example.JobWorker.FileDownload.S3.Services;

internal class S3FileDownloadService(IS3DownloadService s3DownloadService, IS3BucketSource bucketSource)
    : IFileDownloadService
{
    public Task<FileDownloadReport> DownloadAsync(string fromPath, string writePath,
        CancellationToken cancellationToken = default)
    {
        return s3DownloadService.DownloadAsync(bucketSource.Bucket, fromPath, writePath, cancellationToken);
    }
}