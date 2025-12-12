using RedShirt.Example.JobWorker.FileDownload.Core.Models;

namespace RedShirt.Example.JobWorker.FileDownload.Core.Services;

public interface IFileDownloadService
{
    Task<FileDownloadReport> DownloadAsync(string fromPath, string writePath,
        CancellationToken cancellationToken = default);
}