using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class RemoteCacheShortTermIteratorStorageTests
{
    private static readonly TimeSpan ExpectedExpiry = TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Test_Get_WithNoResult()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);

        var key = Guid.NewGuid().ToString();
        string? value = null;

        remoteCache.Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(value);

        var storage = new RemoteCacheShortTermIteratorStorage(remoteCache.Object);
        var storedValue = await storage.GetAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, storedValue);

        Assert.Single(remoteCache.Invocations);
        remoteCache.Verify(
            c => c.GetStringAsync(It.Is<string>(k => k.Contains(key)), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_Get_WithResult()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(value);

        var storage = new RemoteCacheShortTermIteratorStorage(remoteCache.Object);
        var storedValue = await storage.GetAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal(value, storedValue);

        Assert.Single(remoteCache.Invocations);
        remoteCache.Verify(
            c => c.GetStringAsync(It.Is<string>(k => k.Contains(key)), TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_Set()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        remoteCache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();

        var storage = new RemoteCacheShortTermIteratorStorage(remoteCache.Object);
        await storage.SetAsync(key, value, TestContext.Current.CancellationToken);

        Assert.Single(remoteCache.Invocations);
        remoteCache.Verify(
            c => c.SetStringAsync(It.Is<string>(k => k.Contains(key)), value, ExpectedExpiry,
                TestContext.Current.CancellationToken), Times.Once);
    }

    /// <summary>
    ///     Setting a null value to the key should still be delegated to the remote cache service.
    /// </summary>
    [Fact]
    public async Task Test_Set_Null()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        remoteCache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var key = Guid.NewGuid().ToString();
        string? value = null;

        var storage = new RemoteCacheShortTermIteratorStorage(remoteCache.Object);
        await storage.SetAsync(key, value, TestContext.Current.CancellationToken);

        Assert.Single(remoteCache.Invocations);
        remoteCache.Verify(
            c => c.SetStringAsync(It.Is<string>(k => k.Contains(key)), null, ExpectedExpiry,
                TestContext.Current.CancellationToken), Times.Once);
    }
}