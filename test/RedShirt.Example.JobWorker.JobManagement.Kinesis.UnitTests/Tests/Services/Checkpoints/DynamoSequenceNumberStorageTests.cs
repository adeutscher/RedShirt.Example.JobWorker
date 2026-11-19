using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services.Checkpoints;

public class DynamoSequenceNumberStorageTests
{
    [Fact]
    public async Task Test_Get()
    {
        var ctx = new Mock<IDynamoDBContext>();
        var value = Guid.NewGuid().ToString();
        ctx.Setup(c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DynamoSequenceNumberStorage.Record
            {
                Value = value
            });

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }), KinesisTestOptions.Create());

        var result = await storage.GetLastSequenceNumber("foo", TestContext.Current.CancellationToken);
        Assert.Equal(value, result);

        ctx.Verify(
            c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(
            c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<LoadConfig>(), TestContext.Current.CancellationToken), Times.Once);

        var invocation = Assert.Single(ctx.Invocations);
        var record = invocation.Arguments[0] as string;
        Assert.NotNull(record);
        Assert.NotEqual("foo", record);
        Assert.Contains("foo", record);
        var opConfig = invocation.Arguments[1] as LoadConfig;
        Assert.NotNull(opConfig);
        Assert.Equal(tableName, opConfig.OverrideTableName);
    }

    [Fact]
    public async Task Test_Get_Fail()
    {
        var ctx = new Mock<IDynamoDBContext>();

        using var cts = new CancellationTokenSource();

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }), KinesisTestOptions.Create());

        var result = await storage.GetLastSequenceNumber("foo", TestContext.Current.CancellationToken);
        Assert.Null(result);

        // Nothing under the current key, so the legacy key is read as well. Both miss.
        Assert.Equal(2, ctx.Invocations.Count);
        Assert.Equal(KeyHelper.GetCheckpointKey(KinesisTestOptions.StreamArn, "foo"), ctx.Invocations[0].Arguments[0]);
        Assert.Equal(KeyHelper.GetLegacyCheckpointKey("foo"), ctx.Invocations[1].Arguments[0]);
        Assert.All(ctx.Invocations, invocation =>
        {
            var opConfig = invocation.Arguments[1] as LoadConfig;
            Assert.NotNull(opConfig);
            Assert.Equal(tableName, opConfig.OverrideTableName);
            Assert.Equal(TestContext.Current.CancellationToken, invocation.Arguments[2]);
        });
    }

    [Fact]
    public async Task Test_Get_CurrentKeyIncludesStreamName_AndSkipsLegacyWhenFound()
    {
        var ctx = new Mock<IDynamoDBContext>(MockBehavior.Strict);
        var currentKey = KeyHelper.GetCheckpointKey(KinesisTestOptions.StreamArn, "shardId-000000000001");
        ctx.Setup(c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(currentKey, It.IsAny<LoadConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DynamoSequenceNumberStorage.Record {Value = "current"});

        var storage = new DynamoSequenceNumberStorage(ctx.Object, new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = "table",
                RecordDurationHours = 1
            }), KinesisTestOptions.Create());

        var result = await storage.GetLastSequenceNumber("shardId-000000000001",
            TestContext.Current.CancellationToken);

        Assert.Equal("current", result);
        Assert.Contains($":{KinesisTestOptions.StreamName}:shardId-000000000001", currentKey);
        Assert.Single(ctx.Invocations);
    }

    [Fact]
    public async Task Test_Get_FallsBackToLegacyKey()
    {
        var ctx = new Mock<IDynamoDBContext>(MockBehavior.Strict);
        ctx.Setup(c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(
                KeyHelper.GetCheckpointKey(KinesisTestOptions.StreamArn, "shard-1"), It.IsAny<LoadConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DynamoSequenceNumberStorage.Record?) null);
        ctx.Setup(c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(
                KeyHelper.GetLegacyCheckpointKey("shard-1"), It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DynamoSequenceNumberStorage.Record {Value = "legacy"});

        var storage = new DynamoSequenceNumberStorage(ctx.Object, new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = "table",
                RecordDurationHours = 1
            }), KinesisTestOptions.Create());

        var result = await storage.GetLastSequenceNumber("shard-1", TestContext.Current.CancellationToken);

        Assert.Equal("legacy", result);
    }

    [Fact]
    public async Task Test_Set_WritesCurrentKeyOnly()
    {
        var ctx = new Mock<IDynamoDBContext>(MockBehavior.Strict);
        DynamoSequenceNumberStorage.Record? saved = null;
        ctx.Setup(c => c.SaveAsync(It.IsAny<DynamoSequenceNumberStorage.Record>(), It.IsAny<SaveConfig>(),
                It.IsAny<CancellationToken>()))
            .Callback((DynamoSequenceNumberStorage.Record r, SaveConfig _, CancellationToken _) => saved = r)
            .Returns(Task.CompletedTask);

        var storage = new DynamoSequenceNumberStorage(ctx.Object, new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = "table",
                RecordDurationHours = 1
            }), KinesisTestOptions.Create());

        await storage.SetLastSequenceNumber("shard-1", "42", TestContext.Current.CancellationToken);

        Assert.NotNull(saved);
        Assert.Equal(KeyHelper.GetCheckpointKey(KinesisTestOptions.StreamArn, "shard-1"), saved.ShardId);
        Assert.Single(ctx.Invocations);
    }

    [Fact]
    public async Task Test_Set()
    {
        var ctx = new Mock<IDynamoDBContext>();

        using var cts = new CancellationTokenSource();

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }), KinesisTestOptions.Create());

        await storage.SetLastSequenceNumber("foo", "bar", TestContext.Current.CancellationToken);

        ctx.Verify(
            c => c.SaveAsync(It.IsAny<DynamoSequenceNumberStorage.Record>(), It.IsAny<SaveConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(
            c => c.SaveAsync(It.IsAny<DynamoSequenceNumberStorage.Record>(), It.IsAny<SaveConfig>(),
                TestContext.Current.CancellationToken), Times.Once);

        var invocation = Assert.Single(ctx.Invocations);
        var record = invocation.Arguments[0] as DynamoSequenceNumberStorage.Record;
        Assert.NotNull(record);
        Assert.NotEqual("foo", record.ShardId);
        Assert.Contains("foo", record.ShardId);
        Assert.Equal("bar", record.Value);
        var opConfig = invocation.Arguments[1] as SaveConfig;
        Assert.NotNull(opConfig);
        Assert.Equal(tableName, opConfig.OverrideTableName);
    }

    private sealed class PassthroughRetryWrapper : IKinesisRetryWrapperService
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }

        public Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }
    }
}