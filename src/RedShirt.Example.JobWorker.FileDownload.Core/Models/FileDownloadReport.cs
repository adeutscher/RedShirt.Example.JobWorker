namespace RedShirt.Example.JobWorker.FileDownload.Core.Models;

public class FileDownloadReport
{
    public required string FilePath { get; init; }
    public required long FileSize { get; init; }
    public required string Md5 { get; init; }
    public required string Sha1 { get; init; }
    public required string Sha256 { get; init; }
    public required string Sha512 { get; init; }
}