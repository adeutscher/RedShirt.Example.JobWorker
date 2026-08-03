using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Idempotency;

public class IdempotencyExecutionServiceTests
{
    private static IdempotencyConfigurationModel CreateOptions(
        bool enabled = true,
        int resultCacheDurationSeconds = 30,
        bool idempotencyIdsCanRepeat = false)
    {
        return new IdempotencyConfigurationModel
        {
            Enabled = enabled,
            ResultCacheDurationSeconds = resultCacheDurationSeconds,
            MonitorIntervalSeconds = 5,
            IdempotencyIdsCanRepeat = idempotencyIdsCanRepeat
        };
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
            .ReturnsAsync(cachedValue);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions()), NullLogger<IdempotencyExecutionService>.Instance);

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
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
            .ReturnsAsync(cachedValue);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions()), NullLogger<IdempotencyExecutionService>.Instance);

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(jobResult, result!.JobResult);
        Assert.Equal(acknowledgedSuccessfully, result.AcknowledgementResult.AcknowledgedSuccessfully);
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

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(enabled)), NullLogger<IdempotencyExecutionService>.Instance);

        var result = await service.GetCachedResultAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(cache.Invocations);
    }

    [Fact]
    public async Task GetLockAsync_WhenEnabledWithIdempotencyId_DelegatesToLockService()
    {
        var expectedLock = new Mock<ISafeAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);
        expectedLock.SetupGet(l => l.IsTrulyAcquired).Returns(true);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(expectedLock.Object);

        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var job = CreateJob();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions()), NullLogger<IdempotencyExecutionService>.Instance);

        var result = await service.GetLockAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Same(expectedLock.Object, result);
        lockService.Verify(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken),
            Times.Once);
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

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(enabled)), NullLogger<IdempotencyExecutionService>.Instance);

        var result = await service.GetLockAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        await result.UnlockAsync(TestContext.Current.CancellationToken);
        Assert.Empty(lockService.Invocations);
        Assert.Empty(cache.Invocations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLockAsync_WhenLockNotTrulyAcquired_LogsTrace(bool isTrulyAcquired)
    {
        var expectedLock = new Mock<ISafeAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);
        expectedLock.SetupGet(l => l.IsTrulyAcquired).Returns(isTrulyAcquired);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(expectedLock.Object);

        var logger = new Mock<ILogger<IdempotencyExecutionService>>();
        logger.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(true);
        var service = new IdempotencyExecutionService(lockService.Object,
            new Mock<ISafeRemoteCacheService>(MockBehavior.Strict).Object,
            Options.Create(CreateOptions()), logger.Object);

        await service.GetLockAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        logger.Verify(
            l => l.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("idem-1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            isTrulyAcquired ? Times.Never() : Times.Once());
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
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: idempotencyIdsCanRepeat)),
            NullLogger<IdempotencyExecutionService>.Instance);

        await service.SetResultInCacheAsync(CreateRawJob().Object, jobResult,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = acknowledgementSuccess,
                LoggedFailureSuccessfully = null
            }, TestContext.Current.CancellationToken);

        cache.Verify(
            c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken), Times.Once);
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
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(resultCacheDurationSeconds: 1)),
            NullLogger<IdempotencyExecutionService>.Instance);

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
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: false)),
            NullLogger<IdempotencyExecutionService>.Instance);

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
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: true)),
            NullLogger<IdempotencyExecutionService>.Instance);

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
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: false)),
            NullLogger<IdempotencyExecutionService>.Instance);

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

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(enabled)), NullLogger<IdempotencyExecutionService>.Instance);

        await service.SetResultInCacheAsync(CreateRawJob(idempotencyId).Object, CoreJobResult.Success,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        Assert.Empty(cache.Invocations);
    }
}