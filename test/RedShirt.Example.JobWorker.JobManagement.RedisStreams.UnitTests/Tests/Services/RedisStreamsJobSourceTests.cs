using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Models;
using RedShirt.Example.JobWorker.JobManagement.RedisStreams.Services;
using StackExchange.Redis;
using System.Reflection;

namespace RedShirt.Example.JobWorker.JobManagement.RedisStreams.UnitTests.Tests.Services;

public class RedisStreamsJobSourceTests
{
    private const long DefaultIdleMilliseconds =
        RedisStreamsJobSource.ConfigurationModel.DefaultPendingReclaimIdleSeconds * 1000L;

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

    /// <summary>
    ///     StreamPendingMessageInfo has no public constructor, so tests build it through reflection.
    /// </summary>
    private static StreamPendingMessageInfo CreatePending(string id, int deliveryCount, string consumer = "dead-1")
    {
        var constructor = typeof(StreamPendingMessageInfo)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length == 4);
        return (StreamPendingMessageInfo) constructor.Invoke([
            (RedisValue) id, (RedisValue) consumer, DefaultIdleMilliseconds + 1, deliveryCount
        ]);
    }

    private static RedisStreamsJobSource.ConfigurationModel CreateConfig(
        string stream = "jobs",
        string group = "job-worker",
        string? consumer = "worker-1",
        int? pendingReclaimIdleSeconds = null,
        int? maximumDeliveries = null)
    {
        return new RedisStreamsJobSource.ConfigurationModel
        {
            StreamName = stream,
            GroupName = group,
            ConsumerName = consumer,
            PendingReclaimIdleSeconds = pendingReclaimIdleSeconds,
            MaximumDeliveries = maximumDeliveries
        };
    }

    private static RedisStreamsJobSource CreateJobSource(IDatabase database,
        RedisStreamsJobSource.ConfigurationModel? config = null)
    {
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(database);

        return new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(config ?? CreateConfig()));
    }

    private static void SetupPending(Mock<IDatabase> database, int count, params StreamPendingMessageInfo[] pending)
    {
        database
            .Setup(d => d.StreamPendingMessagesAsync("jobs", "job-worker", count,
                It.Is<RedisValue>(v => v.IsNull), null, null, DefaultIdleMilliseconds, CommandFlags.None))
            .ReturnsAsync(pending);
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

        var jobSource = CreateJobSource(database.Object);

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
    [InlineData(null, RedisStreamsJobSource.ConfigurationModel.DefaultMaximumDeliveries)]
    [InlineData(0, RedisStreamsJobSource.ConfigurationModel.DefaultMaximumDeliveries)]
    [InlineData(-3, RedisStreamsJobSource.ConfigurationModel.DefaultMaximumDeliveries)]
    [InlineData(1, 1)]
    [InlineData(9, 9)]
    public void EffectiveMaximumDeliveries_FallsBackToDefault(int? configured, int expected)
    {
        Assert.Equal(expected, CreateConfig(maximumDeliveries: configured).EffectiveMaximumDeliveries);
    }

    [Fact]
    public async Task GetJobsAsync_DoesNotClaim_WhenEveryIdleEntryIsAtTheLimit()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 2, CreatePending("1-0", 5));
        database
            .Setup(d => d.StreamAcknowledgeAsync("jobs", "job-worker", It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync(1);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, CommandFlags.None))
            .ReturnsAsync([]);

        var jobSource = CreateJobSource(database.Object);

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        database.Verify(d => d.StreamClaimAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetJobsAsync_DoesNotScanPendingEntries_WhenReclaimIsDisabled(int idleSeconds)
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 4, false, CommandFlags.None))
            .ReturnsAsync([]);

        var jobSource = CreateJobSource(database.Object, CreateConfig(pendingReclaimIdleSeconds: idleSeconds));

        await jobSource.GetJobsAsync(4, TestContext.Current.CancellationToken);

        Assert.Equal(0, jobSource.RecommendedHeartbeatIntervalSeconds);
        database.Verify(d => d.StreamPendingMessagesAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue?>(), It.IsAny<RedisValue?>(),
            It.IsAny<long?>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_DropsEntriesAtTheDeliveryLimit_AndClaimsTheRest()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 3, CreatePending("1-0", 3), CreatePending("1-1", 2), CreatePending("1-2", 7));
        database
            .Setup(d => d.StreamAcknowledgeAsync("jobs", "job-worker",
                It.Is<RedisValue[]>(ids => ids.SequenceEqual(new RedisValue[] {"1-0", "1-2"})),
                CommandFlags.None))
            .ReturnsAsync(2);
        database
            .Setup(d => d.StreamClaimAsync("jobs", "job-worker", "worker-1", DefaultIdleMilliseconds,
                It.Is<RedisValue[]>(ids => ids.SequenceEqual(new RedisValue[] {"1-1"})), CommandFlags.None))
            .ReturnsAsync([CreateEntry("1-1")]);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, CommandFlags.None))
            .ReturnsAsync([]);

        var jobSource = CreateJobSource(database.Object, CreateConfig(maximumDeliveries: 3));

        var response = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        Assert.Equal(["1-1"], response.Items.Select(i => i.MessageId));
        database.VerifyAll();
    }

    [Fact]
    public async Task GetJobsAsync_IgnoresClaimedEntriesThatNoLongerExist()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 2, CreatePending("1-0", 1));
        database
            .Setup(d => d.StreamClaimAsync("jobs", "job-worker", "worker-1", DefaultIdleMilliseconds,
                It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync([StreamEntry.Null]);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, CommandFlags.None))
            .ReturnsAsync([CreateEntry("2-0")]);

        var jobSource = CreateJobSource(database.Object);

        var response = await jobSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(["2-0"], response.Items.Select(i => i.MessageId));
    }

    [Fact]
    public async Task GetJobsAsync_MapsEntriesToRawJobModels()
    {
        var entry1 = CreateEntry("1-0", """{"a":1}""", "idem-1");
        var entry2 = CreateEntry("1-1", """{"b":2}""", "idem-2");

        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 2);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, CommandFlags.None))
            .ReturnsAsync([entry1, entry2]);

        var jobSource = CreateJobSource(database.Object);

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
    public async Task GetJobsAsync_ReclaimsIdleEntries_ThenFillsTheBatchWithNewEntries()
    {
        var stale = CreateEntry("1-0", """{"old":1}""");
        var fresh = CreateEntry("2-0", """{"new":1}""");

        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 3, CreatePending("1-0", 1));
        database
            .Setup(d => d.StreamClaimAsync("jobs", "job-worker", "worker-1", DefaultIdleMilliseconds,
                It.Is<RedisValue[]>(ids => ids.SequenceEqual(new RedisValue[] {"1-0"})), CommandFlags.None))
            .ReturnsAsync([stale]);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 2, false, CommandFlags.None))
            .ReturnsAsync([fresh]);

        var jobSource = CreateJobSource(database.Object);

        var response = await jobSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        Assert.Equal(["1-0", "2-0"], response.Items.Select(i => i.MessageId));
        Assert.Equal("""{"old":1}""", response.Items[0].Body);
    }

    [Fact]
    public async Task GetJobsAsync_ReturnsEmpty_WhenNoEntries()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 5);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", "worker-1", ">", 5, false, CommandFlags.None))
            .ReturnsAsync([]);

        var jobSource = CreateJobSource(database.Object);

        var response = await jobSource.GetJobsAsync(5, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetJobsAsync_SkipsNewRead_WhenReclaimFillsTheBatch()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 1, CreatePending("1-0", 2));
        database
            .Setup(d => d.StreamClaimAsync("jobs", "job-worker", "worker-1", DefaultIdleMilliseconds,
                It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync([CreateEntry("1-0")]);

        var jobSource = CreateJobSource(database.Object);

        var response = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Single(response.Items);
        database.Verify(d => d.StreamReadGroupAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<RedisValue>(), It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_UsesMachineName_WhenConsumerNameUnset()
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        SetupPending(database, 1);
        database
            .Setup(d => d.StreamReadGroupAsync("jobs", "job-worker", Environment.MachineName, ">", 1, false,
                CommandFlags.None))
            .ReturnsAsync([]);

        var jobSource = CreateJobSource(database.Object, CreateConfig(consumer: null));

        await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        database.Verify(
            d => d.StreamReadGroupAsync("jobs", "job-worker", Environment.MachineName, ">", 1, false,
                CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task HeartbeatAsync_IgnoresIncompatibleModels()
    {
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig()));

        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);

        connection.Verify(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp_WhenReclaimIsDisabled()
    {
        var connection = new Mock<IRedisConnectionCacheService>(MockBehavior.Strict);
        var jobSource = new RedisStreamsJobSource(
            connection.Object,
            RedisStreamsRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            new NullLogger<RedisStreamsJobSource>(),
            Options.Create(CreateConfig(pendingReclaimIdleSeconds: 0)));

        await jobSource.HeartbeatAsync(new RedisStreamRawJobModel
        {
            Message = CreateEntry("1-0"),
            MessageId = "1-0",
            CreatedAtUtc = DateTime.UtcNow
        }, TestContext.Current.CancellationToken);

        connection.Verify(c => c.GetDatabaseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HeartbeatAsync_ResetsIdleTime_WithoutCountingADelivery()
    {
        var entry = CreateEntry("1-0");
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database
            .Setup(d => d.StreamClaimIdsOnlyAsync("jobs", "job-worker", "worker-1", 0,
                It.Is<RedisValue[]>(ids => ids.SequenceEqual(new[] {entry.Id})), CommandFlags.None))
            .ReturnsAsync([entry.Id]);

        var jobSource = CreateJobSource(database.Object);

        await jobSource.HeartbeatAsync(new RedisStreamRawJobModel
        {
            Message = entry,
            MessageId = "1-0",
            CreatedAtUtc = DateTime.UtcNow
        }, TestContext.Current.CancellationToken);

        database.VerifyAll();
    }

    [Theory]
    [InlineData(null, 150)]
    [InlineData(60, 30)]
    [InlineData(5, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void RecommendedHeartbeatIntervalSeconds_IsHalfTheReclaimIdleTime(int? idleSeconds, int expected)
    {
        var jobSource = CreateJobSource(new Mock<IDatabase>().Object,
            CreateConfig(pendingReclaimIdleSeconds: idleSeconds));

        Assert.Equal(expected, jobSource.RecommendedHeartbeatIntervalSeconds);
    }
}