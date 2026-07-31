using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class SafeRemoteCacheServiceTests
{
    private static (SafeRemoteCacheService Service, SafetyDisgraceStateService DisgraceState) CreateService(
        Mock<IRemoteCacheService> remoteCache,
        int disgracePeriodSeconds = 60)
    {
        var disgraceState = new SafetyDisgraceStateService(
            Options.Create(new SafetyDisgraceStateService.ConfigurationModel
            {
                DisgracePeriodSeconds = disgracePeriodSeconds
            }));

        var service = new SafeRemoteCacheService(remoteCache.Object, disgraceState,
            new NullLogger<SafeRemoteCacheService>());
        return (service, disgraceState);
    }

    [Fact]
    public async Task DisgraceExpires_AllowsRemoteCallsAgain()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.SetupSequence(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerDistributedException(new TimeoutException("slow")))
            .ReturnsAsync("recovered");

        // Zero-second disgrace ends immediately after the failure call returns.
        var (service, _) = CreateService(remoteCache, 0);
        var first = await service.GetStringAsync(key, TestContext.Current.CancellationToken);
        var second = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Equal("recovered", second);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Exactly(2));
    }

    [Fact]
    public async Task DisgraceFromGet_AlsoSuppressesSet()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromMinutes(1);

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(new WorkerDistributedException(new Exception("offline")));

        var (service, _) = CreateService(remoteCache);
        await service.GetStringAsync(key, TestContext.Current.CancellationToken);
        await service.SetStringAsync(key, "value", expiry, TestContext.Current.CancellationToken);

        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.Verify(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStringAsync_ReturnsValueFromRemoteCache()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync(value);

        var (service, _) = CreateService(remoteCache);
        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, result);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStringAsync_WhenAlreadyInDisgrace_SkipsRemoteCache()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var (service, disgraceState) = CreateService(remoteCache);
        disgraceState.EnterDisgracePeriod();

        var result = await service.GetStringAsync("any-key", TestContext.Current.CancellationToken);

        Assert.Null(result);
        remoteCache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStringAsync_WhenDistributedException_ReturnsNullAndEntersDisgrace(bool isTransient)
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var inner = new Exception("boom");
        var exception = (Exception) Activator.CreateInstance(typeof(WorkerDistributedException), inner, isTransient)!;

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var (service, disgraceState) = CreateService(remoteCache);
        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.True(disgraceState.IsInDisgracePeriod());

        // Still in disgrace: subsequent get must not hit the remote cache.
        var second = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(second);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStringAsync_WhenNonCacheException_Propagates()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("not a cache failure"));

        var (service, _) = CreateService(remoteCache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetStringAsync(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStringAsync_WhenOperationCanceledAndTokenCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        remoteCache.Setup(c => c.GetStringAsync("key", cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var (service, disgraceState) = CreateService(remoteCache);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetStringAsync("key", cts.Token));

        Assert.False(disgraceState.IsInDisgracePeriod());
        remoteCache.Verify(c => c.GetStringAsync("key", cts.Token), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStringAsync_WhenRemoteReturnsNull_ReturnsNull()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync((string?) null);

        var (service, _) = CreateService(remoteCache);
        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(result);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task SetStringAsync_DelegatesToRemoteCache()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromMinutes(2);

        remoteCache.Setup(c => c.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var (service, _) = CreateService(remoteCache);
        await service.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken);

        remoteCache.Verify(c => c.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken),
            Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetStringAsync_WhenAlreadyInDisgrace_SkipsRemoteCache()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var (service, disgraceState) = CreateService(remoteCache);
        disgraceState.EnterDisgracePeriod();

        await service.SetStringAsync("any-key", "value", TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        remoteCache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetStringAsync_WhenDistributedException_SwallowsAndEntersDisgrace(bool isTransient)
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromSeconds(30);
        var inner = new Exception("boom");
        var exception = (Exception) Activator.CreateInstance(typeof(WorkerDistributedException), inner, isTransient)!;

        remoteCache.Setup(c => c.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var (service, disgraceState) = CreateService(remoteCache);
        await service.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken);

        Assert.True(disgraceState.IsInDisgracePeriod());

        // Still in disgrace: subsequent set must not hit the remote cache.
        await service.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken);

        remoteCache.Verify(c => c.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken),
            Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetStringAsync_WhenNonCacheException_Propagates()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromSeconds(5);

        remoteCache.Setup(c => c.SetStringAsync(key, "value", expiry, TestContext.Current.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("not a cache failure"));

        var (service, _) = CreateService(remoteCache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetStringAsync(key, "value", expiry, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetStringAsync_WhenOperationCanceledAndTokenCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var expiry = TimeSpan.FromSeconds(5);
        remoteCache.Setup(c => c.SetStringAsync("key", "value", expiry, cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var (service, disgraceState) = CreateService(remoteCache);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SetStringAsync("key", "value", expiry, cts.Token));

        Assert.False(disgraceState.IsInDisgracePeriod());
        remoteCache.Verify(c => c.SetStringAsync("key", "value", expiry, cts.Token), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }
}