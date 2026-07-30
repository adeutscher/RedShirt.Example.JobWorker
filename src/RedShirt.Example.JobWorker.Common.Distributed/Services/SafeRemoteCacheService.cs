using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

public class SafeRemoteCacheService(
    IRemoteCacheService remoteCacheService,
    IOptions<SafeRemoteCacheService.ConfigurationModel> options) : ISafeRemoteCacheService
{
    private readonly Lock _disgraceLock = new();
    private DateTimeOffset? _disgraceUntil;

    private bool IsInDisgracePeriod()
    {
        lock (_disgraceLock)
        {
            return _disgraceUntil is { } until && DateTimeOffset.UtcNow < until;
        }
    }

    private void EnterDisgracePeriod()
    {
        lock (_disgraceLock)
        {
            _disgraceUntil = DateTimeOffset.UtcNow.AddSeconds(options.Value.DisgracePeriodSeconds);
        }
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        if (IsInDisgracePeriod())
        {
            return null;
        }

        try
        {
            return await remoteCacheService.GetStringAsync(key, cancellationToken);
        }
        catch (CacheException)
        {
            EnterDisgracePeriod();
            return null;
        }
    }

    public async Task SetStringAsync(string? key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (IsInDisgracePeriod())
        {
            return;
        }

        try
        {
            await remoteCacheService.SetStringAsync(key, value, expiry, cancellationToken);
        }
        catch (CacheException)
        {
            EnterDisgracePeriod();
        }
    }

    public sealed class ConfigurationModel
    {
        public required int DisgracePeriodSeconds { get; init; }
    }
}