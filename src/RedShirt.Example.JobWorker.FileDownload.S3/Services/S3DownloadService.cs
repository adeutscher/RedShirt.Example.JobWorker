using Amazon.S3;
using Amazon.S3.Model;
using RedShirt.Example.JobWorker.FileDownload.Core.Models;
using System.Security.Cryptography;

namespace RedShirt.Example.JobWorker.FileDownload.S3.Services;

internal interface IS3DownloadService
{
    public Task<FileDownloadReport> DownloadAsync(string bucketName, string key, string fileName,
        CancellationToken cancellationToken = default);
}

internal class S3DownloadService(IAmazonS3 s3) : IS3DownloadService
{
    public async Task<FileDownloadReport> DownloadAsync(string bucketName, string key, string fileName,
        CancellationToken cancellationToken = default)
    {
        using var md5 = MD5.Create();
        using var sha1 = SHA1.Create();
        using var sha256 = SHA256.Create();
        using var sha512 = SHA512.Create();

        const int buflen = 8192;
        var buffer = new byte[buflen];

        var bytesRead = 0;
        long bytesReadTotal = 0;

        using (var getResponse = await s3.GetObjectAsync(new GetObjectRequest
               {
                   BucketName = bucketName,
                   Key = key
               }, cancellationToken))
        {
            await using (var fileStream = File.OpenWrite(fileName))
            {
                while ((bytesRead =
                           await getResponse.ResponseStream.ReadAsync(buffer.AsMemory(0, buflen), cancellationToken)) >
                       0)
                {
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha512.TransformBlock(buffer, 0, bytesRead, null, 0);

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    bytesReadTotal += bytesRead;
                }
            }
        }

        md5.TransformFinalBlock([], 0, 0);
        sha1.TransformFinalBlock([], 0, 0);
        sha256.TransformFinalBlock([], 0, 0);
        sha512.TransformFinalBlock([], 0, 0);

        return new FileDownloadReport
        {
            FilePath = fileName,
            FileSize = bytesReadTotal,
            Md5 = BitConverter.ToString(md5.Hash!).Replace("-", "").ToLowerInvariant(),
            Sha1 = BitConverter.ToString(sha1.Hash!).Replace("-", "").ToLowerInvariant(),
            Sha256 = BitConverter.ToString(sha256.Hash!).Replace("-", "").ToLowerInvariant(),
            Sha512 = BitConverter.ToString(sha512.Hash!).Replace("-", "").ToLowerInvariant()
        };
    }
}