using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Distributed.Enums;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

internal class SafeRemoteCacheService(
    IRemoteCacheService remoteCacheService,
    ISafetyDisgraceStateService safetyDisgraceStateService,
    ILogger<SafeRemoteCacheService> logger) : ISafeRemoteCacheService
{
    public async Task<SafeDistributedGetOperationResponse<string?>> GetStringAsync(string key,
        CancellationToken cancellationToken = default)
    {
        if (safetyDisgraceStateService.IsInDisgracePeriod(out var nextAttemptTime))
        {
            return new SafeDistributedGetOperationResponse<string?>
            {
                Result = SafeDistributedOperationResult.DisgracePeriod,
                NextAttemptTime = nextAttemptTime,
                Value = null
            };
        }

        try
        {
            var value = await remoteCacheService.GetStringAsync(key, cancellationToken);
            return new SafeDistributedGetOperationResponse<string?>
            {
                Result = SafeDistributedOperationResult.Success,
                NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime(),
                Value = value
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkerDistributedException e)
        {
            logger.LogWarning(e, "Failure to communicate with cache service: {EMessage}", e.Message);
            safetyDisgraceStateService.EnterDisgracePeriod();
            return new SafeDistributedGetOperationResponse<string?>
            {
                Result = SafeDistributedOperationResult.Failure,
                NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime(),
                Value = null
            };
        }
    }

    public async Task<SafeDistributedOperationResponse> SetStringAsync(string key, string? value, TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (safetyDisgraceStateService.IsInDisgracePeriod(out var nextAttemptTime))
        {
            return new SafeDistributedOperationResponse
            {
                Result = SafeDistributedOperationResult.DisgracePeriod,
                NextAttemptTime = nextAttemptTime
            };
        }

        try
        {
            await remoteCacheService.SetStringAsync(key, value, expiry, cancellationToken);
            return new SafeDistributedOperationResponse
            {
                Result = SafeDistributedOperationResult.Success,
                NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkerDistributedException e)
        {
            logger.LogWarning(e, "Failure to communicate with cache service: {EMessage}", e.Message);
            safetyDisgraceStateService.EnterDisgracePeriod();
            return new SafeDistributedOperationResponse
            {
                Result = SafeDistributedOperationResult.Failure,
                NextAttemptTime = safetyDisgraceStateService.GetNextAttemptTime()
            };
        }
    }
}