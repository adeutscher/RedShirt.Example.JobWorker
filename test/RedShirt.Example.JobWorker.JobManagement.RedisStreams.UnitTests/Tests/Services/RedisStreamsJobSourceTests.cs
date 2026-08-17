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
        string? consumer = "worker-1",
        int waitTimeSeconds = 0)
    {
        return new RedisStreamsJobSource.ConfigurationModel
        {
            StreamName = stream,
            GroupName = group,
            ConsumerName = consumer,
            WaitTimeSeconds = waitTimeSeconds
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

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    [InlineData(60, 30)]
    public void EffectiveWaitTimeSeconds_ClampsToZeroThroughThirty(int configured, int expected)
    {
        var options = CreateConfig(waitTimeSeconds: configured);

        Assert.Equal(expected, options.EffectiveWaitTimeSeconds);
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
        database.Verify(
            d => d.StreamReadGroupAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>(),
                It.IsAny<CommandFlags>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetJobsAsync_WaitTimeSecondsNonPositive_UsesNonBlockingOverload(int waitTimeSeconds)
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 3, false, CommandFlags.None))
            .ReturnsAsync([]);

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database.Object);

        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig(waitTimeSeconds: waitTimeSeconds)));

        var response = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        database.Verify(
            d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 3, false, CommandFlags.None),
            Times.Once);
        database.Verify(
            d => d.StreamReadGroupAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>(),
                It.IsAny<CommandFlags>()), Times.Never);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(45, 30)]
    [InlineData(60, 30)]
    public async Task GetJobsAsync_WaitTimeSecondsPositive_UsesBlockingOverloadWithCappedTimeout(
        int waitTimeSeconds, int expectedBlockSeconds)
    {
        var entry = CreateEntry("1-0");
        var expectedBlock = TimeSpan.FromSeconds(expectedBlockSeconds);

        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, expectedBlock,
                CommandFlags.None))
            .ReturnsAsync([entry]);

        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database.Object);

        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig(waitTimeSeconds: waitTimeSeconds)));

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        var item = Assert.Single(response.Items);
        Assert.Equal("1-0", item.MessageId);
        database.Verify(
            d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, expectedBlock,
                CommandFlags.None), Times.Once);
        database.Verify(
            d => d.StreamReadGroupAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), CommandFlags.None), Times.Never);
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
}