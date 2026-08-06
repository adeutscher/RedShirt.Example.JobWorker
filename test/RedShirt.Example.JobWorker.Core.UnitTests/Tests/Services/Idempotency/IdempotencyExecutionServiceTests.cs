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
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Idempotency;

public class IdempotencyExecutionServiceTests
{
    private static ICoreHealthStateUpdateService CreateHealthStateUpdateService()
    {
        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());
        return health.Object;
    }

    private static IdempotencyConfigurationModel CreateOptions(
        bool enabled = true,
        int resultCacheDurationSeconds = 30,
        bool idempotencyIdsCanRepeat = false,
        bool enableTraceLogging = false)
    {
        return new IdempotencyConfigurationModel
        {
            Enabled = enabled,
            ResultCacheDurationSeconds = resultCacheDurationSeconds,
            MonitorIntervalSeconds = 5,
            IdempotencyIdsCanRepeat = idempotencyIdsCanRepeat,
            EnableTraceLogging = enableTraceLogging
        };
    }

    private static Mock<ILogger<IdempotencyExecutionService>> CreateLogger(bool enableTraceLevel = true)
    {
        var logger = new Mock<ILogger<IdempotencyExecutionService>>();
        logger.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(enableTraceLevel);
        logger.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);
        return logger;
    }

    private static void VerifyNoTraceLogs(Mock<ILogger<IdempotencyExecutionService>> logger)
    {
        logger.Verify(
            l => l.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private static void VerifyTraceLogContains(Mock<ILogger<IdempotencyExecutionService>> logger,
        string expectedFragment, Times times)
    {
        logger.Verify(
            l => l.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(expectedFragment)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    private static Mock<IJobModel> CreateJob(string? idempotencyId = "idem-1")
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.IdempotencyId).Returns(idempotencyId);
        return job;
    }

    private static Mock<IRawJobModel> CreateRawJob(string? idempotencyId = "idem-1")
    {
        var raw = new Mock<IRawJobModel>(MockBehavior.Strict);
        raw.Setup(r => r.IdempotencyId).Returns(idempotencyId);
        return raw;
    }

    private static string SerializeCacheReport(CoreJobResult result, bool acknowledgedSuccessfully,
        bool? loggedFailureSuccessfully = null)
    {
        return JsonSerializer.Serialize(new IdempotencyExecutionService.CachedAcknowledgeReport
        {
            Result = result,
            AcknowledgedSuccessfully = acknowledgedSuccessfully,
            LoggedFailureSuccessfully = loggedFailureSuccessfully
        }, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static SafeDistributedGetOperationResponse<string?> CacheGetSuccess(string? value)
    {
        return new SafeDistributedGetOperationResponse<string?>
        {
            Result = SafeDistributedOperationResult.Success,
            NextAttemptTime = DateTime.UtcNow,
            Value = value
        };
    }

    private static SafeDistributedOperationResponse CacheSetSuccess()
    {
        return new SafeDistributedOperationResponse
        {
            Result = SafeDistributedOperationResult.Success,
            NextAttemptTime = DateTime.UtcNow
        };
    }

    private static SafeDistributedLockOperationResponse LockResponse(
        IAbstractedLock abstractedLock,
        SafeDistributedOperationResult result = SafeDistributedOperationResult.Success)
    {
        return new SafeDistributedLockOperationResponse
        {
            Result = result,
            NextAttemptTime = DateTime.UtcNow,
            Lock = abstractedLock
        };
    }

    [Fact]
    public async Task GetCachedResultAsync_WhenCacheThrows_AndHaltOnFailure_Propagates()
    {
        var unexpected = new InvalidOperationException("cache backend unavailable");

        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetStringAsync("idempotency:idem-1:result", TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object,
            cache.Object,
            health.Object,
            Options.Create(CreateOptions()),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            CreateLogger().Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public async Task GetCachedResultAsync_WhenCacheValueIsNotValid_ReturnsNull(string? cachedValue)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetStringAsync("idempotency:idem-1:result", TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheGetSuccess(cachedValue));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions()),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public async Task GetCachedResultAsync_WhenCacheValueIsNotValid_WithTraceLogging_LogsGettingValue(
        string? cachedValue)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetStringAsync("idempotency:idem-1:result", TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheGetSuccess(cachedValue));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
        VerifyTraceLogContains(logger, "IdempotencyExecutionService.GetCachedResultAsync", Times.Once());
        VerifyTraceLogContains(logger, "getting value", Times.Once());
        VerifyTraceLogContains(logger, "idempotency:idem-1:result", Times.Once());
    }

    [Theory]
    [InlineData(CoreJobResult.Success, true)]
    [InlineData(CoreJobResult.Failure, false)]
    public async Task GetCachedResultAsync_WhenCacheValueIsValid_ReturnsParsedValue(CoreJobResult jobResult,
        bool acknowledgedSuccessfully)
    {
        var cachedValue = SerializeCacheReport(jobResult, acknowledgedSuccessfully);
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetStringAsync("idempotency:idem-1:result", TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheGetSuccess(cachedValue));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions()),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(jobResult, result!.JobResult);
        Assert.Equal(acknowledgedSuccessfully, result.AcknowledgementResult.AcknowledgedSuccessfully);
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(CoreJobResult.Success, true)]
    [InlineData(CoreJobResult.Failure, false)]
    public async Task GetCachedResultAsync_WhenCacheValueIsValid_WithTraceLogging_LogsGettingValue(
        CoreJobResult jobResult, bool acknowledgedSuccessfully)
    {
        var cachedValue = SerializeCacheReport(jobResult, acknowledgedSuccessfully);
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetStringAsync("idempotency:idem-1:result", TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheGetSuccess(cachedValue));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        VerifyTraceLogContains(logger, "IdempotencyExecutionService.GetCachedResultAsync", Times.Once());
        VerifyTraceLogContains(logger, "getting value", Times.Once());
        VerifyTraceLogContains(logger, "idempotency:idem-1:result", Times.Once());
    }

    [Theory]
    [InlineData(false, "idem-1")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task GetCachedResultAsync_WhenIdempotencyCannotProceed_ReturnsNull(bool enabled,
        string? idempotencyId)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var job = CreateJob(idempotencyId);
        var logger = CreateLogger();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enabled)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetCachedResultAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(cache.Invocations);
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(false, "idem-1", "Disabled")]
    [InlineData(true, null, "EmptyIdempotencyKey")]
    [InlineData(true, "", "EmptyIdempotencyKey")]
    [InlineData(true, "   ", "EmptyIdempotencyKey")]
    public async Task GetCachedResultAsync_WhenIdempotencyCannotProceed_WithTraceLogging_LogsReason(bool enabled,
        string? idempotencyId, string expectedReason)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var logger = CreateLogger();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enabled, enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetCachedResultAsync(CreateJob(idempotencyId).Object,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(cache.Invocations);
        VerifyTraceLogContains(logger, "IdempotencyExecutionService.GetCachedResultAsync", Times.Once());
        VerifyTraceLogContains(logger, "cannot proceed", Times.Once());
        VerifyTraceLogContains(logger, expectedReason, Times.Once());
    }

    [Fact]
    public async Task GetLockAsync_WhenEnabledWithIdempotencyId_DelegatesToLockService()
    {
        var expectedLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(LockResponse(expectedLock.Object));

        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var job = CreateJob();
        var logger = CreateLogger();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions()),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetLockAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Same(expectedLock.Object, result);
        lockService.Verify(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken),
            Times.Once);
        VerifyNoTraceLogs(logger);
    }

    [Fact]
    public async Task GetLockAsync_WhenEnabledWithIdempotencyId_WithTraceLogging_LogsAcquireAttempts()
    {
        var expectedLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(LockResponse(expectedLock.Object));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.GetLockAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        VerifyTraceLogContains(logger, "IdempotencyExecutionService.GetLockAsync", Times.Exactly(2));
        VerifyTraceLogContains(logger, "acquiring lock", Times.Once());
        VerifyTraceLogContains(logger, "finished attempting lock", Times.Once());
        VerifyTraceLogContains(logger, "idempotency:idem-1:lock", Times.Exactly(2));
    }

    [Theory]
    [InlineData(false, "idem-1")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task GetLockAsync_WhenIdempotencyCannotProceed_ReturnsAcquiredEmptyLock(bool enabled,
        string? idempotencyId)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var job = CreateJob(idempotencyId);
        var logger = CreateLogger();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enabled)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetLockAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        await result.UnlockAsync(TestContext.Current.CancellationToken);
        Assert.Empty(lockService.Invocations);
        Assert.Empty(cache.Invocations);
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(false, "idem-1", "Disabled")]
    [InlineData(true, null, "EmptyIdempotencyKey")]
    [InlineData(true, "", "EmptyIdempotencyKey")]
    [InlineData(true, "   ", "EmptyIdempotencyKey")]
    public async Task GetLockAsync_WhenIdempotencyCannotProceed_WithTraceLogging_LogsReason(bool enabled,
        string? idempotencyId, string expectedReason)
    {
        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enabled, enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        var result = await service.GetLockAsync(CreateJob(idempotencyId).Object,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        VerifyTraceLogContains(logger, "IdempotencyExecutionService.GetLockAsync", Times.Once());
        VerifyTraceLogContains(logger, "cannot proceed", Times.Once());
        VerifyTraceLogContains(logger, expectedReason, Times.Once());
    }

    [Theory]
    [InlineData(SafeDistributedOperationResult.Success)]
    [InlineData(SafeDistributedOperationResult.Failure)]
    [InlineData(SafeDistributedOperationResult.DisgracePeriod)]
    public async Task GetLockAsync_WhenLockResultIsNotSuccess_LogsWarning(
        SafeDistributedOperationResult lockResult)
    {
        var expectedLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(LockResponse(expectedLock.Object, lockResult));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions()),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.GetLockAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("idem-1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            lockResult == SafeDistributedOperationResult.Success ? Times.Never() : Times.Once());
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(SafeDistributedOperationResult.Success)]
    [InlineData(SafeDistributedOperationResult.Failure)]
    [InlineData(SafeDistributedOperationResult.DisgracePeriod)]
    public async Task GetLockAsync_WhenLockResultIsNotSuccess_WithTraceLogging_StillLogsWarningAndTraces(
        SafeDistributedOperationResult lockResult)
    {
        var expectedLock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(LockResponse(expectedLock.Object, lockResult));

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.GetLockAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        VerifyTraceLogContains(logger, "acquiring lock", Times.Once());
        VerifyTraceLogContains(logger, "finished attempting lock", Times.Once());
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("idem-1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            lockResult == SafeDistributedOperationResult.Success ? Times.Never() : Times.Once());
    }

    [Fact]
    public async Task GetLockAsync_WhenLockServiceThrows_AndHaltOnFailure_Propagates()
    {
        var unexpected = new InvalidOperationException("lock backend unavailable");

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var service = new IdempotencyExecutionService(
            lockService.Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            health.Object,
            Options.Create(CreateOptions()),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            CreateLogger().Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetLockAsync(CreateJob().Object, TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Success, false, true)]
    [InlineData(CoreJobResult.Failure, false, true)]
    [InlineData(CoreJobResult.Success, true, true)]
    [InlineData(CoreJobResult.Failure, true, true)]
    [InlineData(CoreJobResult.Success, false, false)]
    [InlineData(CoreJobResult.Failure, false, false)]
    public async Task SetResultInCacheAsync_Otherwise_StoresResultString(CoreJobResult jobResult,
        bool acknowledgementSuccess, bool idempotencyIdsCanRepeat)
    {
        if (acknowledgementSuccess && !idempotencyIdsCanRepeat)
        {
            // Successful acknowledgement with non-repeatable ids clears cache instead of storing.
            return;
        }

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var expectedPayload = SerializeCacheReport(jobResult, acknowledgementSuccess);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(),
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: idempotencyIdsCanRepeat)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, jobResult,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = acknowledgementSuccess,
                LoggedFailureSuccessfully = null
            }, TestContext.Current.CancellationToken);

        cache.Verify(
            c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken), Times.Once);
        VerifyNoTraceLogs(logger);
    }

    [Fact]
    public async Task SetResultInCacheAsync_UsesEffectiveResultCacheDurationSeconds()
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var expectedPayload = SerializeCacheReport(CoreJobResult.Success, false);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(resultCacheDurationSeconds: 1)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, CoreJobResult.Success,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = false,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        cache.Verify(
            c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken), Times.Once);
        VerifyNoTraceLogs(logger);
    }

    [Fact]
    public async Task SetResultInCacheAsync_WhenAcknowledgementNotFullySuccessful_StoresEvenIfIdsCannotRepeat()
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var expectedPayload = SerializeCacheReport(CoreJobResult.Failure, true, false);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(idempotencyIdsCanRepeat: false)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, CoreJobResult.Failure,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = false
            },
            TestContext.Current.CancellationToken);

        cache.Verify(
            c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken), Times.Once);
        VerifyNoTraceLogs(logger);
    }

    [Fact]
    public async Task SetResultInCacheAsync_WhenAcknowledgementSucceededAndIdsCanRepeat_StoresResultString()
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var expectedPayload = SerializeCacheReport(CoreJobResult.Success, true);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(idempotencyIdsCanRepeat: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, CoreJobResult.Success,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        cache.Verify(
            c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken), Times.Once);
        VerifyNoTraceLogs(logger);
    }

    [Fact]
    public async Task SetResultInCacheAsync_WhenAcknowledgementSucceededAndIdsCanRepeat_WithTraceLogging_LogsSet()
    {
        var expectedPayload = SerializeCacheReport(CoreJobResult.Success, true);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object, cache.Object,
            CreateHealthStateUpdateService(),
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: true, enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, CoreJobResult.Success,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        VerifyTraceLogContains(logger, "IdempotencyExecutionService.SetResultInCacheAsync", Times.Exactly(2));
        VerifyTraceLogContains(logger, "setting value", Times.Once());
        VerifyTraceLogContains(logger, "set value", Times.Once());
        VerifyTraceLogContains(logger, expectedPayload, Times.Once());
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task SetResultInCacheAsync_WhenAcknowledgementSucceededAndIdsCannotRepeat_ClearsCache(
        CoreJobResult jobResult)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", null, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(idempotencyIdsCanRepeat: false)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, jobResult,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        cache.Verify(
            c => c.SetStringAsync("idempotency:idem-1:result", null, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken), Times.Once);
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task SetResultInCacheAsync_WhenAcknowledgementSucceededAndIdsCannotRepeat_WithTraceLogging_LogsClear(
        CoreJobResult jobResult)
    {
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", null, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(CacheSetSuccess());

        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object, cache.Object,
            CreateHealthStateUpdateService(),
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: false, enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob().Object, jobResult,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        VerifyTraceLogContains(logger, "IdempotencyExecutionService.SetResultInCacheAsync", Times.Exactly(2));
        VerifyTraceLogContains(logger, "clearing value", Times.Once());
        VerifyTraceLogContains(logger, "cleared value", Times.Once());
        VerifyTraceLogContains(logger, "idempotency:idem-1:result", Times.Exactly(2));
    }

    [Fact]
    public async Task SetResultInCacheAsync_WhenClearThrows_AndHaltOnFailure_Propagates()
    {
        var unexpected = new InvalidOperationException("cache backend unavailable");

        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.SetStringAsync(
                "idempotency:idem-1:result",
                null,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object,
            cache.Object,
            health.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: false)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            CreateLogger().Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetResultInCacheAsync(
                CreateRawJob().Object,
                CoreJobResult.Success,
                new SafeAcknowledgementResult
                {
                    AcknowledgedSuccessfully = true,
                    LoggedFailureSuccessfully = null
                },
                TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }

    [Theory]
    [InlineData(false, "idem-1")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task SetResultInCacheAsync_WhenIdempotencyCannotProceed_DoesNotTouchCache(bool enabled,
        string? idempotencyId)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var logger = CreateLogger();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enabled)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob(idempotencyId).Object, CoreJobResult.Success,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        Assert.Empty(cache.Invocations);
        VerifyNoTraceLogs(logger);
    }

    [Theory]
    [InlineData(false, "idem-1", "Disabled")]
    [InlineData(true, null, "EmptyIdempotencyKey")]
    [InlineData(true, "", "EmptyIdempotencyKey")]
    [InlineData(true, "   ", "EmptyIdempotencyKey")]
    public async Task SetResultInCacheAsync_WhenIdempotencyCannotProceed_WithTraceLogging_LogsReason(bool enabled,
        string? idempotencyId, string expectedReason)
    {
        var logger = CreateLogger();
        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            CreateHealthStateUpdateService(), Options.Create(CreateOptions(enabled, enableTraceLogging: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}), logger.Object);

        await service.SetResultInCacheAsync(CreateRawJob(idempotencyId).Object, CoreJobResult.Success,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        VerifyTraceLogContains(logger, "IdempotencyExecutionService.SetResultInCacheAsync", Times.Once());
        VerifyTraceLogContains(logger, "cannot proceed", Times.Once());
        VerifyTraceLogContains(logger, expectedReason, Times.Once());
    }

    [Fact]
    public async Task SetResultInCacheAsync_WhenSetThrows_AndHaltOnFailure_Propagates()
    {
        var unexpected = new InvalidOperationException("cache backend unavailable");

        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.SetStringAsync(
                "idempotency:idem-1:result",
                It.IsAny<string>(),
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .ThrowsAsync(unexpected);

        var health = new Mock<ICoreHealthStateUpdateService>(MockBehavior.Strict);
        health.Setup(h => h.NoteIncident());

        var service = new IdempotencyExecutionService(
            new Mock<ISafeAbstractedLockService>(MockBehavior.Strict).Object,
            cache.Object,
            health.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: true)),
            Options.Create(new CoreConfigurationModel {HaltOnFailure = true}),
            CreateLogger().Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetResultInCacheAsync(
                CreateRawJob().Object,
                CoreJobResult.Success,
                new SafeAcknowledgementResult
                {
                    AcknowledgedSuccessfully = true,
                    LoggedFailureSuccessfully = null
                },
                TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
        health.Verify(h => h.NoteIncident(), Times.Once);
    }
}