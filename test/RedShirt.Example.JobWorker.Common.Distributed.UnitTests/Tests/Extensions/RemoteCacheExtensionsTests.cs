using System.Text.Json;
using Moq;
using RedShirt.Example.JobWorker.Common.Distributed.Extensions;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Distributed.UnitTests.Tests.Extensions;

public class RemoteCacheExtensionsTests
{
    [Fact]
    public async Task GetObjectAsync_DeserializesValidJson()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var expected = new SampleCacheObject { Name = "alpha", Count = 7 };

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync(JsonSerializer.Serialize(expected));

        var result = await remoteCache.Object.GetObjectAsync<SampleCacheObject>(key,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expected.Name, result.Name);
        Assert.Equal(expected.Count, result.Count);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task GetObjectAsync_WhenValueMissingOrBlank_ReturnsNull(string? cachedValue)
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedValue);

        var result = await remoteCache.Object.GetObjectAsync<SampleCacheObject>(key,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{")]
    [InlineData("123")]
    [InlineData("\"just-a-string\"")]
    public async Task GetObjectAsync_WhenJsonInvalidForType_ReturnsNull(string cachedValue)
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();

        remoteCache.Setup(c => c.GetStringAsync(key, TestContext.Current.CancellationToken))
            .ReturnsAsync(cachedValue);

        var result = await remoteCache.Object.GetObjectAsync<SampleCacheObject>(key,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        remoteCache.Verify(c => c.GetStringAsync(key, TestContext.Current.CancellationToken), Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetObjectAsync_SerializesAndWritesToRemoteCache()
    {
        var remoteCache = new Mock<IRemoteCacheService>(MockBehavior.Strict);
        var key = Guid.NewGuid().ToString();
        var value = new SampleCacheObject { Name = "beta", Count = 42 };
        var expiry = TimeSpan.FromMinutes(5);
        var expectedJson = JsonSerializer.Serialize(value);

        remoteCache.Setup(c =>
                c.SetStringAsync(key, expectedJson, expiry, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        await remoteCache.Object.SetObjectAsync(key, value, expiry, TestContext.Current.CancellationToken);

        remoteCache.Verify(
            c => c.SetStringAsync(key, expectedJson, expiry, TestContext.Current.CancellationToken),
            Times.Once);
        remoteCache.VerifyNoOtherCalls();
    }

    private sealed class SampleCacheObject
    {
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
    }
}
