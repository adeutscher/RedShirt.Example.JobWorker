using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

internal interface IIdempotencyResultCacheService
{
    Task<bool?> GetResultAsync(IJobModel jobModel, CancellationToken cancellationToken = default);
    Task SetResultAsync(IJobModel jobModel, bool result, CancellationToken cancellationToken = default);
}

internal class IdempotencyResultCacheService(
    ISafeRemoteCacheService cache,
    IOptions<IdempotencyConfigurationModel> options)
    : IIdempotencyResultCacheService
{
    private bool IdempotencyCannotProceed(IJobModel jobModel)
    {
        return !options.Value.Enabled || string.IsNullOrWhiteSpace(jobModel.IdempotencyId);
    }

    private string GetKey(IJobModel jobModel)
    {
        return $"idempotency:result:{jobModel.IdempotencyId}";
    }

    public async Task<bool?> GetResultAsync(IJobModel jobModel, CancellationToken cancellationToken = default)
    {
        if (IdempotencyCannotProceed(jobModel))
        {
            return null;
        }

        var rawResult = await cache.GetStringAsync(GetKey(jobModel), cancellationToken);

        if (!bool.TryParse(rawResult, out var result))
        {
            return null;
        }

        return result;
    }

    public Task SetResultAsync(IJobModel jobModel, bool result, CancellationToken cancellationToken = default)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (IdempotencyCannotProceed(jobModel))
        {
            return Task.CompletedTask;
        }

        return cache.SetStringAsync(GetKey(jobModel), result.ToString(),
            TimeSpan.FromSeconds(options.Value.ResultCacheDurationSeconds), cancellationToken);
    }
}