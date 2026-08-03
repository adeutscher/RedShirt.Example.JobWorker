using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

internal interface IIdempotencyExecutionService
{
    Task<IdempotencyCacheResult?> GetCachedResultAsync(IJobModel jobModel,
        CancellationToken cancellationToken = default);

    Task<IAbstractedLock> GetLockAsync(IJobModel jobModel, CancellationToken token);

    Task SetResultInCacheAsync(IRawJobModel jobModel, CoreJobResult jobResult,
        ISafeAcknowledgementResult acknowledgementResult,
        CancellationToken cancellationToken = default);
}

internal sealed class IdempotencyExecutionService(
    ISafeAbstractedLockService abstractedLockService,
    ISafeRemoteCacheService cache,
    IOptions<IdempotencyConfigurationModel> options,
    ILogger<IdempotencyExecutionService> logger) : IIdempotencyExecutionService
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

        var @lock = await abstractedLockService.GetLockAsync(GetLockKey(jobModel.IdempotencyId!), token);

        if (!@lock.IsTrulyAcquired)
        {
            logger.LogTrace(
                "Idempotency lock for {IdempotencyId} was not truly acquired; proceeding with a permissive lock",
                jobModel.IdempotencyId);
        }

        return @lock;
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
            JobResult = cachedResult.Result,
            AcknowledgementResult = new SafeAcknowledgementResult
            {
                LoggedFailureSuccessfully = cachedResult.LoggedFailureSuccessfully,
                AcknowledgedSuccessfully = cachedResult.AcknowledgedSuccessfully
            }
        };
    }

    public Task SetResultInCacheAsync(IRawJobModel jobModel, CoreJobResult jobResult,
        ISafeAcknowledgementResult acknowledgementResult,
        CancellationToken cancellationToken = default)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (IdempotencyCannotProceed(jobModel.IdempotencyId))
        {
            return Task.CompletedTask;
        }

        var timeSpan = TimeSpan.FromSeconds(options.Value.EffectiveResultCacheDurationSeconds);

        if (acknowledgementResult.Success && !options.Value.IdempotencyIdsCanRepeat)
        {
            // If the idempotency IDs cannot repeat,
            //   then it can be reasonably assumed that there's no point in caching the data
            //   set to null to delete data in underlying cache in an effort to save resources.
            return cache.SetStringAsync(GetResultKey(jobModel.IdempotencyId!), null, timeSpan, cancellationToken);
        }

        return cache.SetStringAsync(GetResultKey(jobModel.IdempotencyId!), JsonSerializer.Serialize(
            new CachedAcknowledgeReport
            {
                Result = jobResult,
                LoggedFailureSuccessfully = acknowledgementResult.LoggedFailureSuccessfully,
                AcknowledgedSuccessfully = acknowledgementResult.AcknowledgedSuccessfully
            }, JsonOptions), timeSpan, cancellationToken);
    }

    internal sealed class CachedAcknowledgeReport
    {
        [JsonPropertyName("r")]
        public CoreJobResult Result { get; init; }

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