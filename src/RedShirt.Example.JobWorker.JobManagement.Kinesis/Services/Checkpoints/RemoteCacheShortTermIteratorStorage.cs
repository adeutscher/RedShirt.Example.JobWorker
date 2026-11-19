using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;

internal interface IShortTermIteratorStorage
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

/// <summary>
///     Short-lived shard iterators in the remote cache.
///     There is no fallback to the key format used before the stream name was added: iterators expire within minutes,
///     and a missing iterator falls back to the long-term sequence number checkpoint.
/// </summary>
internal class RemoteCacheShortTermIteratorStorage(
    IRemoteCacheService remoteCacheService,
    IKinesisRetryWrapperService retryWrapperService,
    IOptions<KinesisConfiguration> kinesisOptions)
    : IShortTermIteratorStorage
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            ct => remoteCacheService.GetStringAsync(
                KeyHelper.GetCheckpointKey(kinesisOptions.Value.StreamArn, key), ct),
            cancellationToken);
    }

    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            ct => remoteCacheService.SetStringAsync(KeyHelper.GetCheckpointKey(kinesisOptions.Value.StreamArn, key),
                value, TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5), ct),
            cancellationToken);
    }
}