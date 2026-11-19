using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
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

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Mock.Of<IAmazonDynamoDB>(MockBehavior.Strict),
            new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }), KinesisTestOptions.Create(), NullLogger<DynamoSequenceNumberStorage>.Instance);

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
    public async Task Test_Get_CurrentKeyIncludesStreamName_AndSkipsLegacyWhenFound()
    {
        var ctx = new Mock<IDynamoDBContext>(MockBehavior.Strict);
        var currentKey = KeyHelper.GetCheckpointKey(KinesisTestOptions.StreamArn, "shardId-000000000001");
        ctx.Setup(c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(currentKey, It.IsAny<LoadConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DynamoSequenceNumberStorage.Record {Value = "current"});

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Mock.Of<IAmazonDynamoDB>(MockBehavior.Strict),
            new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = "table",
                RecordDurationHours = 1
            }), KinesisTestOptions.Create(), NullLogger<DynamoSequenceNumberStorage>.Instance);

        var result = await storage.GetLastSequenceNumber("shardId-000000000001",
            TestContext.Current.CancellationToken);

        Assert.Equal("current", result);
        Assert.Contains($":{KinesisTestOptions.StreamName}:shardId-000000000001", currentKey);
        Assert.Single(ctx.Invocations);
    }

    [Fact]
    public async Task Test_Get_Fail()
    {
        var ctx = new Mock<IDynamoDBContext>();

        using var cts = new CancellationTokenSource();

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Mock.Of<IAmazonDynamoDB>(MockBehavior.Strict),
            new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }), KinesisTestOptions.Create(), NullLogger<DynamoSequenceNumberStorage>.Instance);

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

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Mock.Of<IAmazonDynamoDB>(MockBehavior.Strict),
            new PassthroughRetryWrapper(), Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = "table",
                RecordDurationHours = 1
            }), KinesisTestOptions.Create(), NullLogger<DynamoSequenceNumberStorage>.Instance);

        var result = await storage.GetLastSequenceNumber("shard-1", TestContext.Current.CancellationToken);

        Assert.Equal("legacy", result);
    }

    private static (DynamoSequenceNumberStorage Storage, Mock<IAmazonDynamoDB> DynamoDb, Mock<IDynamoDBContext> Context)
        CreateForSet(string tableName = "table")
    {
        var ctx = new Mock<IDynamoDBContext>(MockBehavior.Strict);
        var dynamoDb = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var storage = new DynamoSequenceNumberStorage(ctx.Object, dynamoDb.Object, new PassthroughRetryWrapper(),
            Options.Create(new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 1
            }), KinesisTestOptions.Create(), NullLogger<DynamoSequenceNumberStorage>.Instance);
        return (storage, dynamoDb, ctx);
    }

    [Fact]
    public async Task Test_Set_ConditionalPutUnderCurrentKey()
    {
        var tableName = Guid.NewGuid().ToString();
        var (storage, dynamoDb, ctx) = CreateForSet(tableName);
        PutItemRequest? request = null;
        dynamoDb.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), TestContext.Current.CancellationToken))
            .Callback((PutItemRequest r, CancellationToken _) => request = r)
            .ReturnsAsync(new PutItemResponse());

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await storage.SetLastSequenceNumber("shard-1", "42", TestContext.Current.CancellationToken);

        Assert.NotNull(request);
        Assert.Equal(tableName, request.TableName);
        Assert.Equal(KeyHelper.GetCheckpointKey(KinesisTestOptions.StreamArn, "shard-1"),
            request.Item[nameof(DynamoSequenceNumberStorage.Record.ShardId)].S);
        Assert.Equal("42", request.Item[nameof(DynamoSequenceNumberStorage.Record.Value)].S);
        var expiration = long.Parse(request.Item[nameof(DynamoSequenceNumberStorage.Record.ExpirationTime)].N);
        Assert.InRange(expiration, before + 3600 - 5, before + 3600 + 5);

        var sortable = request.Item[DynamoSequenceNumberStorage.SortableValueAttribute].S;
        Assert.Equal(DynamoSequenceNumberStorage.SortableValueWidth, sortable.Length);
        Assert.EndsWith("42", sortable);

        Assert.Equal("attribute_not_exists(#sortable) OR #sortable <= :sortable", request.ConditionExpression);
        Assert.Equal(DynamoSequenceNumberStorage.SortableValueAttribute, request.ExpressionAttributeNames["#sortable"]);
        Assert.Equal(sortable, request.ExpressionAttributeValues[":sortable"].S);

        // Writes go through the low-level client only.
        Assert.Empty(ctx.Invocations);
    }

    [Fact]
    public async Task Test_Set_ConditionFailed_KeepsNewerCheckpointWithoutThrowing()
    {
        var (storage, dynamoDb, _) = CreateForSet();
        dynamoDb.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("newer checkpoint exists"));

        await storage.SetLastSequenceNumber("shard-1", "42", TestContext.Current.CancellationToken);

        dynamoDb.Verify(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Test_Set_OtherDynamoFailure_Propagates()
    {
        var (storage, dynamoDb, _) = CreateForSet();
        dynamoDb.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("boom"));

        await Assert.ThrowsAsync<AmazonDynamoDBException>(() =>
            storage.SetLastSequenceNumber("shard-1", "42", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("-1")]
    [InlineData("4 2")]
    public async Task Test_Set_NonNumericValue_SavedWithoutCondition(string value)
    {
        var (storage, dynamoDb, _) = CreateForSet();
        PutItemRequest? request = null;
        dynamoDb.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback((PutItemRequest r, CancellationToken _) => request = r)
            .ReturnsAsync(new PutItemResponse());

        await storage.SetLastSequenceNumber("shard-1", value, TestContext.Current.CancellationToken);

        Assert.NotNull(request);
        Assert.Equal(value, request.Item[nameof(DynamoSequenceNumberStorage.Record.Value)].S);
        Assert.False(request.Item.ContainsKey(DynamoSequenceNumberStorage.SortableValueAttribute));
        Assert.Null(request.ConditionExpression);
    }

    [Theory]
    // Kinesis sequence numbers vary in length, so plain string comparison would get these wrong.
    [InlineData("9", "10")]
    [InlineData("49590338271490256608559692538361571095921575989136588898",
        "49590338271490256608559692540925702759324208523137515618")]
    [InlineData("99999999999999999999999999999999999999999999999999999999",
        "100000000000000000000000000000000000000000000000000000000")]
    [InlineData("0", "1")]
    public void ToSortableValue_StringOrderMatchesNumericOrder(string lower, string higher)
    {
        var lowerSortable = DynamoSequenceNumberStorage.ToSortableValue(lower);
        var higherSortable = DynamoSequenceNumberStorage.ToSortableValue(higher);

        Assert.NotNull(lowerSortable);
        Assert.NotNull(higherSortable);
        Assert.True(string.CompareOrdinal(lowerSortable, higherSortable) < 0);
    }

    [Fact]
    public void ToSortableValue_RejectsValuesThatDoNotFit()
    {
        Assert.Null(DynamoSequenceNumberStorage.ToSortableValue(""));
        Assert.Null(DynamoSequenceNumberStorage.ToSortableValue("12a"));
        Assert.Null(DynamoSequenceNumberStorage.ToSortableValue("\u0661\u0662"));
        Assert.Null(DynamoSequenceNumberStorage.ToSortableValue(new string('9', DynamoSequenceNumberStorage.SortableValueWidth + 1)));
        Assert.Equal(new string('9', DynamoSequenceNumberStorage.SortableValueWidth),
            DynamoSequenceNumberStorage.ToSortableValue(new string('9', DynamoSequenceNumberStorage.SortableValueWidth)));
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