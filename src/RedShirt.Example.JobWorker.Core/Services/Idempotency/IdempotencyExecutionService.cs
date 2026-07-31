using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

internal interface IIdempotencyExecutionService
{
    Task<bool?> GetCachedResultAsync(IJobModel jobModel, CancellationToken cancellationToken = default);
    Task<IAbstractedLock> GetLockAsync(IJobModel jobModel, CancellationToken token);

    Task SetResultInCacheAsync(IJobModel jobModel, bool result, bool acknowledgementSuccess,
        CancellationToken cancellationToken = default);
}

internal class IdempotencyExecutionService(
    ISafeAbstractedLockService abstractedLockService,
    ISafeRemoteCacheService cache,
    IOptions<IdempotencyConfigurationModel> options) : IIdempotencyExecutionService
{
    private const string CommonKeyPrefix = "idempotency";

    private bool IdempotencyCannotProceed(IJobModel jobModel)
    {
        return !options.Value.Enabled || string.IsNullOrWhiteSpace(jobModel.IdempotencyId);
    }

    private static string GetKey(IJobModel jobModel, string type)
    {
        return $"{CommonKeyPrefix}:{jobModel.IdempotencyId}:{type}";
    }

    private static string GetLockKey(IJobModel jobModel)
    {
        return GetKey(jobModel, "lock");
    }

    private static string GetResultKey(IJobModel jobModel)
    {
        return GetKey(jobModel, "result");
    }

    public async Task<IAbstractedLock> GetLockAsync(IJobModel jobModel, CancellationToken token)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(jobModel.IdempotencyId))
        {
            /*
             * The method invoking this method doesn't need to be aware of the ins and outs of idempotency.
             * All it needs to know is that it's clear to proceed with execution.
             */
            return new EmptyIdempotencyLock();
        }

        return await abstractedLockService.GetLockAsync(GetLockKey(jobModel), token);
    }

    public async Task<bool?> GetCachedResultAsync(IJobModel jobModel, CancellationToken cancellationToken = default)
    {
        if (IdempotencyCannotProceed(jobModel))
        {
            return null;
        }

        var rawResult = await cache.GetStringAsync(GetResultKey(jobModel), cancellationToken);

        if (!bool.TryParse(rawResult, out var result))
        {
            return null;
        }

        return result;
    }

    public Task SetResultInCacheAsync(IJobModel jobModel, bool result, bool acknowledgementSuccess,
        CancellationToken cancellationToken = default)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (IdempotencyCannotProceed(jobModel))
        {
            return Task.CompletedTask;
        }

        var timeSpan = TimeSpan.FromSeconds(options.Value.EffectiveResultCacheDurationSeconds);

        if (acknowledgementSuccess && options.Value.IdempotencyIdsCanRepeat)
        {
            return cache.SetStringAsync(GetResultKey(jobModel), null, timeSpan, cancellationToken);
        }

        return cache.SetStringAsync(GetResultKey(jobModel), result.ToString(), timeSpan, cancellationToken);
    }

    /// <summary>
    ///     Fakes a successful lock acquisition in order to simplify handling elsewhere.
    /// </summary>
    private sealed class EmptyIdempotencyLock : IAbstractedLock
    {
        public bool IsAcquired => true;

        public Task UnlockAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}