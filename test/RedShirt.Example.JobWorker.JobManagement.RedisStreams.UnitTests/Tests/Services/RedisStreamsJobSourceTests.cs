using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;
using StackExchange.Redis;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

public class RedisStreamsJobSourceTests
{
    private static StreamEntry CreateEntry(string id, string? body = "{\"foo\":1}", string? messageId = null)
    {
        var values = new List<NameValueEntry>();
        if (body is not null)
        {
            values.Add(new NameValueEntry("body", body));
        }

        if (messageId is not null)
        {
            values.Add(new NameValueEntry("message_id", messageId));
        }

        return new StreamEntry(id, values.ToArray());
    }

    private static RedisStreamsJobSource.ConfigurationModel CreateConfig(
        string stream = "jobs",
        string group = "job-worker",
        string? consumer = "worker-1")
    {
        return new RedisStreamsJobSource.ConfigurationModel
        {
            StreamName = stream,
            GroupName = group,
            ConsumerName = consumer
        };
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    [InlineData(CoreJobResult.InvalidData)]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    public async Task AcknowledgeAsync_AlwaysAcknowledges(CoreJobResult result)
    {
        var entry = CreateEntry("1-0");
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamAcknowledgeAsync("jobs", "job-worker", entry.Id, CommandFlags.None))
            .ReturnsAsync(1);

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database.Object);

        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        await jobSource.AcknowledgeAsync(new RedisStreamRawJobModel
        {
            Message = entry,
            MessageId = "1-0",
            CreatedAtUtc = DateTime.UtcNow
        }, result, TestContext.Current.CancellationToken);

        database.Verify(d => d.StreamAcknowledgeAsync("jobs", "job-worker", entry.Id, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AcknowledgeAsync_IgnoresIncompatibleModels()
    {
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        await jobSource.AcknowledgeAsync(new Mock<IRawJobModel>().Object, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        connection.Verify(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_MapsEntriesToRawJobModels()
    {
        var entry1 = CreateEntry("1-0", """{"a":1}""", "idem-1");
        var entry2 = CreateEntry("1-1", """{"b":2}""", "idem-2");

        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, CommandFlags.None))
            .ReturnsAsync([entry1, entry2]);

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database.Object);

        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.All(response.Items, item => Assert.IsType<RedisStreamRawJobModel>(item));
        Assert.Equal("1-0", response.Items[0].MessageId);
        Assert.Equal("idem-1", response.Items[0].IdempotencyId);
        Assert.Equal("""{"a":1}""", response.Items[0].Body);
        Assert.Equal("1-1", response.Items[1].MessageId);
        Assert.Equal("idem-2", response.Items[1].IdempotencyId);
    }

    [Fact]
    public async Task GetJobsAsync_ReturnsEmpty_WhenNoEntries()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 5, false, CommandFlags.None))
            .ReturnsAsync([]);

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database.Object);

        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task GetJobsAsync_UsesMachineName_WhenConsumerNameUnset()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", Environment.MachineName, ">", 1, false,
                CommandFlags.None))
            .ReturnsAsync([]);

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database.Object);

        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig(consumer: null)));

        await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        database.Verify(
            d => d.StreamReadGroupAsync("jobs", "job-worker", Environment.MachineName, ">", 1, false,
                CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var jobSource = new RedisStreamsJobSource(
            new Mock<IRedisConnectionCacheService>().Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);
        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public void StopSubscriber_ThrowsNotSupportedException()
    {
        var jobSource = new RedisStreamsJobSource(
            new Mock<IRedisConnectionCacheService>().Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        Assert.False(jobSource.IsSubscriptionSource);
        Assert.Throws<NotSupportedException>(jobSource.StopSubscriber);
    }
}