using Amazon.DynamoDBv2.DataModel;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

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
            }));

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
            }));

        var result = await storage.GetLastSequenceNumber("foo", TestContext.Current.CancellationToken);
        Assert.Null(result);

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
            }));

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