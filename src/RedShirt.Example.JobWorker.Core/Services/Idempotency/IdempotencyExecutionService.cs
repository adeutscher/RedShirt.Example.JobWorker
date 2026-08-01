using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

internal interface IIdempotencyExecutionService
{
    Task<IdempotencyCacheResult?> GetCachedResultAsync(IJobModel jobModel,
        CancellationToken cancellationToken = default);

    Task<IAbstractedLock> GetLockAsync(IJobModel jobModel, CancellationToken token);

    Task SetResultInCacheAsync(IRawJobModel jobModel, bool jobSuccess, ISafeAcknowledgementResult acknowledgementResult,
        CancellationToken cancellationToken = default);
}

internal class IdempotencyExecutionService(
    ISafeAbstractedLockService abstractedLockService,
    ISafeRemoteCacheService cache,
    IOptions<IdempotencyConfigurationModel> options) : IIdempotencyExecutionService
{
    private const string CommonKeyPrefix = "idempotency";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private bool IdempotencyCannotProceed(string? idempotencyId)
    {
        return !options.Value.Enabled || string.IsNullOrWhiteSpace(idempotencyId);
    }

    private static string GetKey(string idempotencyId, string type)
    {
        return $"{CommonKeyPrefix}:{idempotencyId}:{type}";
    }

    private static string GetLockKey(string idempotencyId)
    {
        return GetKey(idempotencyId, "lock");
    }

    private static string GetResultKey(string idempotencyId)
    {
        return GetKey(idempotencyId, "result");
    }

    private static CachedAcknowledgeReport? Deserialize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CachedAcknowledgeReport>(input, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
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

        return await abstractedLockService.GetLockAsync(GetLockKey(jobModel.IdempotencyId!), token);
    }

    public async Task<IdempotencyCacheResult?> GetCachedResultAsync(IJobModel jobModel,
        CancellationToken cancellationToken = default)
    {
        if (IdempotencyCannotProceed(jobModel.IdempotencyId))
        {
            return null;
        }

        var rawResult = await cache.GetStringAsync(GetResultKey(jobModel.IdempotencyId!), cancellationToken);

        if (Deserialize(rawResult) is not { } cachedResult)
        {
            return null;
        }

        return new IdempotencyCacheResult
        {
            JobSuccess = cachedResult.TaskSuccess,
            AcknowledgementResult = new SafeAcknowledgementResult
            {
                LoggedFailureSuccessfully = cachedResult.LoggedFailureSuccessfully,
                AcknowledgedSuccessfully = cachedResult.AcknowledgedSuccessfully
            }
        };
    }

    public Task SetResultInCacheAsync(IRawJobModel jobModel, bool jobSuccess,
        ISafeAcknowledgementResult acknowledgementResult,
        CancellationToken cancellationToken = default)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (IdempotencyCannotProceed(jobModel.IdempotencyId))
        {
            return Task.CompletedTask;
        }

        var timeSpan = TimeSpan.FromSeconds(options.Value.EffectiveResultCacheDurationSeconds);

        if (acknowledgementResult.Success && options.Value.IdempotencyIdsCanRepeat)
        {
            return cache.SetStringAsync(GetResultKey(jobModel.IdempotencyId!), null, timeSpan, cancellationToken);
        }

        return cache.SetStringAsync(GetResultKey(jobModel.IdempotencyId!), JsonSerializer.Serialize(
            new CachedAcknowledgeReport
            {
                TaskSuccess = jobSuccess,
                LoggedFailureSuccessfully = acknowledgementResult.LoggedFailureSuccessfully,
                AcknowledgedSuccessfully = acknowledgementResult.AcknowledgedSuccessfully
            }, JsonOptions), timeSpan, cancellationToken);
    }

    internal class CachedAcknowledgeReport
    {
        [JsonPropertyName("s")]
        public bool TaskSuccess { get; init; }

        [JsonPropertyName("f")]
        public bool? LoggedFailureSuccessfully { get; init; }

        [JsonPropertyName("a")]
        public bool AcknowledgedSuccessfully { get; init; }
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