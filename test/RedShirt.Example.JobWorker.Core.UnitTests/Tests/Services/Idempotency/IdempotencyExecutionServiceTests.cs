using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
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

    private static string SerializeCacheReport(bool taskSuccess, bool acknowledgedSuccessfully,
        bool? loggedFailureSuccessfully = null)
    {
        return JsonSerializer.Serialize(new IdempotencyExecutionService.CachedAcknowledgeReport
        {
            TaskSuccess = taskSuccess,
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
            Options.Create(CreateOptions()));

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task GetCachedResultAsync_WhenCacheValueIsValid_ReturnsParsedValue(bool taskSuccess,
        bool acknowledgedSuccessfully)
    {
        var cachedValue = SerializeCacheReport(taskSuccess, acknowledgedSuccessfully);
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.GetStringAsync("idempotency:idem-1:result", TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedValue);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions()));

        var result = await service.GetCachedResultAsync(CreateJob().Object, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(taskSuccess, result!.JobSuccess);
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
            Options.Create(CreateOptions(enabled)));

        var result = await service.GetCachedResultAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(cache.Invocations);
    }

    [Fact]
    public async Task GetLockAsync_WhenEnabledWithIdempotencyId_DelegatesToLockService()
    {
        var expectedLock = new Mock<ISafeAbstractedLock>(MockBehavior.Strict);
        expectedLock.SetupGet(l => l.IsAcquired).Returns(true);

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        lockService
            .Setup(s => s.GetLockAsync("idempotency:idem-1:lock", TestContext.Current.CancellationToken))
            .ReturnsAsync(expectedLock.Object);

        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var job = CreateJob();

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions()));

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
            Options.Create(CreateOptions(enabled)));

        var result = await service.GetLockAsync(job.Object, TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        await result.UnlockAsync(TestContext.Current.CancellationToken);
        Assert.Empty(lockService.Invocations);
        Assert.Empty(cache.Invocations);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task SetResultInCacheAsync_Otherwise_StoresResultString(bool jobSuccess,
        bool acknowledgementSuccess, bool idempotencyIdsCanRepeat)
    {
        if (acknowledgementSuccess && idempotencyIdsCanRepeat)
        {
            // Successful acknowledgement with repeatable ids clears cache instead of storing.
            return;
        }

        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        var expectedPayload = SerializeCacheReport(jobSuccess, acknowledgementSuccess);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: idempotencyIdsCanRepeat)));

        await service.SetResultInCacheAsync(CreateRawJob().Object, jobSuccess,
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
        var expectedPayload = SerializeCacheReport(true, false);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", expectedPayload, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(resultCacheDurationSeconds: 1)));

        await service.SetResultInCacheAsync(CreateRawJob().Object, true,
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetResultInCacheAsync_WhenAcknowledgementSucceededAndIdsCanRepeat_ClearsCache(bool jobSuccess)
    {
        var lockService = new Mock<ISafeAbstractedLockService>(MockBehavior.Strict);
        var cache = new Mock<ISafeRemoteCacheService>(MockBehavior.Strict);
        cache
            .Setup(c => c.SetStringAsync("idempotency:idem-1:result", null, TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new IdempotencyExecutionService(lockService.Object, cache.Object,
            Options.Create(CreateOptions(idempotencyIdsCanRepeat: true)));

        await service.SetResultInCacheAsync(CreateRawJob().Object, jobSuccess,
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
            Options.Create(CreateOptions(enabled)));

        await service.SetResultInCacheAsync(CreateRawJob(idempotencyId).Object, true,
            new SafeAcknowledgementResult
            {
                AcknowledgedSuccessfully = true,
                LoggedFailureSuccessfully = null
            },
            TestContext.Current.CancellationToken);

        Assert.Empty(cache.Invocations);
    }
}