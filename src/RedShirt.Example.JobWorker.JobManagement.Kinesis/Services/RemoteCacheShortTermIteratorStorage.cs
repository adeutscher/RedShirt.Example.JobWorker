using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface IShortTermIteratorStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

internal class RemoteCacheShortTermIteratorStorage(IRemoteCacheService remoteCacheService)
    : IShortTermIteratorStorage
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return remoteCacheService.GetStringAsync(KeyHelper.GetCheckpointKey(key), cancellationToken);
    }

    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        return remoteCacheService.SetStringAsync(key, value, TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5),
            cancellationToken);
    }
}