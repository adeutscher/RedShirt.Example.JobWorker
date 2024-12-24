using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class DynamoSequenceNumberStorageTests
{
    [Fact]
    public async Task Test_Get()
    {
        var ctx = new Mock<IDynamoDBContext>();
        var value = Guid.NewGuid().ToString();
        ctx.Setup(c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<DynamoDBOperationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DynamoSequenceNumberStorage.Record
            {
                Value = value
            });
        var cts = new CancellationTokenSource();

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }));

        var result = await storage.GetLastSequenceNumber("foo", cts.Token);
        Assert.Equal(value, result);

        ctx.Verify(
            c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<DynamoDBOperationConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(
            c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<DynamoDBOperationConfig>(), cts.Token), Times.Once);

        var invocation = Assert.Single(ctx.Invocations);
        var record = invocation.Arguments[0] as string;
        Assert.NotNull(record);
        Assert.NotEqual("foo", record);
        Assert.Contains("foo", record);
        var opConfig = invocation.Arguments[1] as DynamoDBOperationConfig;
        Assert.NotNull(opConfig);
        Assert.Equal(tableName, opConfig.OverrideTableName);
    }

    [Fact]
    public async Task Test_Get_Fail()
    {
        var ctx = new Mock<IDynamoDBContext>();

        var cts = new CancellationTokenSource();

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }));

        var result = await storage.GetLastSequenceNumber("foo", cts.Token);
        Assert.Null(result);

        ctx.Verify(
            c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<DynamoDBOperationConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(
            c => c.LoadAsync<DynamoSequenceNumberStorage.Record>(It.IsAny<string>(),
                It.IsAny<DynamoDBOperationConfig>(), cts.Token), Times.Once);

        var invocation = Assert.Single(ctx.Invocations);
        var record = invocation.Arguments[0] as string;
        Assert.NotNull(record);
        Assert.NotEqual("foo", record);
        Assert.Contains("foo", record);
        var opConfig = invocation.Arguments[1] as DynamoDBOperationConfig;
        Assert.NotNull(opConfig);
        Assert.Equal(tableName, opConfig.OverrideTableName);
    }

    [Fact]
    public async Task Test_Set()
    {
        var ctx = new Mock<IDynamoDBContext>();

        var cts = new CancellationTokenSource();

        var tableName = Guid.NewGuid().ToString();

        var storage = new DynamoSequenceNumberStorage(ctx.Object, Options.Create(
            new DynamoSequenceNumberStorage.ConfigurationModel
            {
                TableName = tableName,
                RecordDurationHours = 0
            }));

        await storage.SetLastSequenceNumber("foo", "bar", cts.Token);

        ctx.Verify(
            c => c.SaveAsync(It.IsAny<DynamoSequenceNumberStorage.Record>(), It.IsAny<DynamoDBOperationConfig>(),
                It.IsAny<CancellationToken>()), Times.Once);
        ctx.Verify(
            c => c.SaveAsync(It.IsAny<DynamoSequenceNumberStorage.Record>(), It.IsAny<DynamoDBOperationConfig>(),
                cts.Token), Times.Once);

        var invocation = Assert.Single(ctx.Invocations);
        var record = invocation.Arguments[0] as DynamoSequenceNumberStorage.Record;
        Assert.NotNull(record);
        Assert.NotEqual("foo", record.ShardId);
        Assert.Contains("foo", record.ShardId);
        Assert.Equal("bar", record.Value);
        var opConfig = invocation.Arguments[1] as DynamoDBOperationConfig;
        Assert.NotNull(opConfig);
        Assert.Equal(tableName, opConfig.OverrideTableName);
    }
}