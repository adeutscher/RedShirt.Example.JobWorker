using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class SafeRemoteCacheService(
    IRemoteCacheService remoteCacheService,
    ISafetyDisgraceStateService safetyDisgraceStateService) : ISafeRemoteCacheService
{
    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        if (safetyDisgraceStateService.IsInDisgracePeriod())
        {
            return null;
        }

        try
        {
            return await remoteCacheService.GetStringAsync(key, cancellationToken);
        }
        catch (CacheException)
        {
            safetyDisgraceStateService.EnterDisgracePeriod();
            return null;
        }
    }

    public async Task SetStringAsync(string? key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (safetyDisgraceStateService.IsInDisgracePeriod())
        {
            return;
        }

        try
        {
            await remoteCacheService.SetStringAsync(key, value, expiry, cancellationToken);
        }
        catch (CacheException)
        {
            safetyDisgraceStateService.EnterDisgracePeriod();
        }
    }
}