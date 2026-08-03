using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;

internal interface IShortTermIteratorStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

internal class RemoteCacheShortTermIteratorStorage(
    IRemoteCacheService remoteCacheService,
    IKinesisRetryWrapperService retryWrapperService)
    : IShortTermIteratorStorage
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            ct => remoteCacheService.GetStringAsync(KeyHelper.GetCheckpointKey(key), ct),
            cancellationToken);
    }

    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            ct => remoteCacheService.SetStringAsync(KeyHelper.GetCheckpointKey(key), value,
                TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5), ct),
            cancellationToken);
    }
}