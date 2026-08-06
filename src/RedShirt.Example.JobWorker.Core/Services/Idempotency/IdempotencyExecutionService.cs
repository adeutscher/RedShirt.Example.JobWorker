using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Enums;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Models.Safety;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Health;
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
    ICoreHealthStateUpdateService healthStateUpdateService,
    IOptions<IdempotencyConfigurationModel> options,
    IOptions<CoreConfigurationModel> coreOptions,
    ILogger<IdempotencyExecutionService> logger) : IIdempotencyExecutionService
{
    private const string CommonKeyPrefix = "idempotency";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private bool IdempotencyCannotProceed(string? idempotencyId, out IdempotencyCannotProceedReason reason)
    {
        if (!options.Value.Enabled)
        {
            reason = IdempotencyCannotProceedReason.Disabled;
            return true;
        }

        if (string.IsNullOrWhiteSpace(idempotencyId))
        {
            reason = IdempotencyCannotProceedReason.EmptyIdempotencyKey;
            return true;
        }

        reason = default;
        return false;
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
        if (IdempotencyCannotProceed(jobModel.IdempotencyId, out var reason))
        {
            if (options.Value.EnableTraceLogging)
            {
                logger.LogTrace(
                    "{Class}.{Method} cannot proceed with idempotency: {Reason}",
                    nameof(IdempotencyExecutionService), nameof(GetLockAsync), reason);
            }

            /*
             * The method invoking this method doesn't need to be aware of the ins and outs of idempotency.
             * All it needs to know is that it's clear to proceed with execution.
             */
            return new EmptyIdempotencyLock();
        }

        if (options.Value.EnableTraceLogging)
        {
            logger.LogTrace(
                "{Class}.{Method} acquiring lock: {Key}",
                nameof(IdempotencyExecutionService), nameof(GetLockAsync), GetLockKey(jobModel.IdempotencyId!));
        }

        SafeDistributedLockOperationResponse lockResponse;
        try
        {
            lockResponse = await abstractedLockService.GetLockAsync(GetLockKey(jobModel.IdempotencyId!), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            healthStateUpdateService.NoteIncident();
            if (coreOptions.Value.HaltOnFailure)
            {
                throw;
            }

            return new EmptyIdempotencyLock();
        }

        if (options.Value.EnableTraceLogging)
        {
            logger.LogTrace(
                "{Class}.{Method} finished attempting lock: {Key} (result: {Result}, acquired: {IsAcquired})",
                nameof(IdempotencyExecutionService), nameof(GetLockAsync), GetLockKey(jobModel.IdempotencyId!),
                lockResponse.Result, lockResponse.Lock.IsAcquired);
        }

        if (lockResponse.Result != SafeDistributedOperationResult.Success)
        {
            logger.LogWarning(
                "Idempotency lock for {IdempotencyId} was not reliably acquired (result: {Result}); proceeding with a permissive lock",
                jobModel.IdempotencyId, lockResponse.Result);
        }

        return lockResponse.Lock;
    }

    public async Task<IdempotencyCacheResult?> GetCachedResultAsync(IJobModel jobModel,
        CancellationToken cancellationToken = default)
    {
        if (IdempotencyCannotProceed(jobModel.IdempotencyId, out var reason))
        {
            if (options.Value.EnableTraceLogging)
            {
                logger.LogTrace(
                    "{Class}.{Method} cannot proceed with idempotency: {Reason}",
                    nameof(IdempotencyExecutionService), nameof(GetCachedResultAsync), reason);
            }

            return null;
        }

        if (options.Value.EnableTraceLogging)
        {
            logger.LogTrace(
                "{Class}.{Method} getting value: {Key}",
                nameof(IdempotencyExecutionService), nameof(GetCachedResultAsync),
                GetResultKey(jobModel.IdempotencyId!));
        }

        SafeDistributedGetOperationResponse<string> cacheResponse;
        try
        {
            cacheResponse = await cache.GetStringAsync(GetResultKey(jobModel.IdempotencyId!), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            healthStateUpdateService.NoteIncident();
            if (coreOptions.Value.HaltOnFailure)
            {
                throw;
            }

            return null;
        }

        if (cacheResponse.Result != SafeDistributedOperationResult.Success ||
            Deserialize(cacheResponse.Value) is not { } cachedResult)
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

    public async Task SetResultInCacheAsync(IRawJobModel jobModel, CoreJobResult jobResult,
        ISafeAcknowledgementResult acknowledgementResult,
        CancellationToken cancellationToken = default)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (IdempotencyCannotProceed(jobModel.IdempotencyId, out var reason))
        {
            if (options.Value.EnableTraceLogging)
            {
                logger.LogTrace(
                    "{Class}.{Method} cannot proceed with idempotency: {Reason}",
                    nameof(IdempotencyExecutionService), nameof(SetResultInCacheAsync), reason);
            }

            return;
        }

        var timeSpan = TimeSpan.FromSeconds(options.Value.EffectiveResultCacheDurationSeconds);

        if (acknowledgementResult.Success && !options.Value.IdempotencyIdsCanRepeat)
        {
            if (options.Value.EnableTraceLogging)
            {
                logger.LogTrace(
                    "{Class}.{Method} clearing value: {Key}",
                    nameof(IdempotencyExecutionService), nameof(SetResultInCacheAsync),
                    GetResultKey(jobModel.IdempotencyId!));
            }

            // If the idempotency IDs cannot repeat,
            //   then it can be reasonably assumed that there's no point in caching the data
            //   set to null to delete data in underlying cache in an effort to save resources.
            try
            {
                await cache.SetStringAsync(GetResultKey(jobModel.IdempotencyId!), null, timeSpan, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                healthStateUpdateService.NoteIncident();
                if (coreOptions.Value.HaltOnFailure)
                {
                    throw;
                }

                return;
            }

            if (options.Value.EnableTraceLogging)
            {
                logger.LogTrace(
                    "{Class}.{Method} cleared value: {Key}",
                    nameof(IdempotencyExecutionService), nameof(SetResultInCacheAsync),
                    GetResultKey(jobModel.IdempotencyId!));
            }

            return;
        }

        var value = JsonSerializer.Serialize(
            new CachedAcknowledgeReport
            {
                Result = jobResult,
                LoggedFailureSuccessfully = acknowledgementResult.LoggedFailureSuccessfully,
                AcknowledgedSuccessfully = acknowledgementResult.AcknowledgedSuccessfully
            }, JsonOptions);

        if (options.Value.EnableTraceLogging)
        {
            logger.LogTrace(
                "{Class}.{Method} setting value: {Key} -> {Value}",
                nameof(IdempotencyExecutionService), nameof(SetResultInCacheAsync),
                GetResultKey(jobModel.IdempotencyId!), value);
        }

        try
        {
            await cache.SetStringAsync(GetResultKey(jobModel.IdempotencyId!), value, timeSpan, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            healthStateUpdateService.NoteIncident();
            if (coreOptions.Value.HaltOnFailure)
            {
                throw;
            }

            return;
        }

        if (options.Value.EnableTraceLogging)
        {
            logger.LogTrace(
                "{Class}.{Method} set value: {Key}",
                nameof(IdempotencyExecutionService), nameof(SetResultInCacheAsync),
                GetResultKey(jobModel.IdempotencyId!));
        }
    }

    private enum IdempotencyCannotProceedReason
    {
        Unknown,
        Disabled,
        EmptyIdempotencyKey
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