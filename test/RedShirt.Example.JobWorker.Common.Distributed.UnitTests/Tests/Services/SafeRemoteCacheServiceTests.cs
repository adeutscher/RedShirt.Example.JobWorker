using Microsoft.Extensions.Options;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Services;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Services;

public class SafeRemoteCacheServiceTests
{
    private static SafeRemoteCacheService CreateService(
        Mock<IRemoteCacheService> remoteCache,
        int disgracePeriodSeconds = 60)
    {
        return new SafeRemoteCacheService(
            remoteCache.Object,
            Options.Create(new SafeRemoteCacheService.ConfigurationModel
            {
                DisgracePeriodSeconds = disgracePeriodSeconds
            }));
    }

    [Fact]
    public async Task GetStringAsync_ReturnsValueFromRemoteCache()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync(value);

        var service = CreateService(remoteCache);
        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, result);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStringAsync_WhenRemoteReturnsNull_ReturnsNull()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync((string?)null);

        var service = CreateService(remoteCache);
        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(result);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(typeof(CacheConnectionException))]
    [InlineData(typeof(CacheTimeoutException))]
    public async Task GetStringAsync_WhenCacheException_ReturnsNullAndEntersDisgrace(Type exceptionType)
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var inner = new Exception("boom");
        var exception = (Exception)Activator.CreateInstance(exceptionType, inner)!;

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var service = CreateService(remoteCache);
        var result = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(result);

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

        var service = CreateService(remoteCache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetStringAsync(key, TestContext.Current.CancellationToken));
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

        var service = CreateService(remoteCache);
        await service.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken);

        remoteCache.Verify(c => c.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken),
            Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(typeof(CacheConnectionException))]
    [InlineData(typeof(CacheTimeoutException))]
    public async Task SetStringAsync_WhenCacheException_SwallowsAndEntersDisgrace(Type exceptionType)
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromSeconds(30);
        var inner = new Exception("boom");
        var exception = (Exception)Activator.CreateInstance(exceptionType, inner)!;

        remoteCache.Setup(c => c.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken))
            .ThrowsAsync(exception);

        var service = CreateService(remoteCache);
        await service.SetStringAsync(key, value, expiry, TestContext.Current.CancellationToken);

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

        var service = CreateService(remoteCache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetStringAsync(key, "value", expiry, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisgraceFromGet_AlsoSuppressesSet()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromMinutes(1);

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(new CacheConnectionException(new Exception("offline")));

        var service = CreateService(remoteCache);
        await service.GetStringAsync(key, TestContext.Current.CancellationToken);
        await service.SetStringAsync(key, "value", expiry, TestContext.Current.CancellationToken);

        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.Verify(c => c.SetStringAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisgraceExpires_AllowsRemoteCallsAgain()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.SetupSequence(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ThrowsAsync(new CacheTimeoutException(new TimeoutException("slow")))
            .ReturnsAsync("recovered");

        // Zero-second disgrace ends immediately after the failure call returns.
        var service = CreateService(remoteCache, disgracePeriodSeconds: 0);
        var first = await service.GetStringAsync(key, TestContext.Current.CancellationToken);
        var second = await service.GetStringAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Equal("recovered", second);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Exactly(2));
    }
}
