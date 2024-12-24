using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class CheckpointStorageTests
{
    [Fact]
    public async Task Test_GetCheckpoint_Fresh()
    {
        var shortTerm = new Mock<IShortTermIteratorStorage>();
        var longTerm = new Mock<ISequenceNumberStorage>();
        var kinesis = new Mock<IAmazonKinesis>();
        var options = new KinesisConfiguration
        {
            BatchSize = 0,
            StreamArn = Guid.NewGuid().ToString()
        };

        var checkpointStorage =
            new CheckpointStorage(shortTerm.Object, longTerm.Object, kinesis.Object, Options.Create(options));

        var iteratorString = Guid.NewGuid().ToString();
        shortTerm.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?) null);
        longTerm.Setup(l => l.GetLastSequenceNumber(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?) null);
        kinesis.Setup(a => a.GetShardIteratorAsync(It.IsAny<GetShardIteratorRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetShardIteratorResponse
            {
                ShardIterator = iteratorString
            });

        var shardId = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();
        var value = await checkpointStorage.GetCheckpointAsync(shardId, cts.Token);

        Assert.Single(shortTerm.Invocations);
        shortTerm.Verify(s => s.GetAsync(shardId, cts.Token), Times.Once);

        Assert.Equal(iteratorString, value);

        Assert.Single(longTerm.Invocations);
        longTerm.Verify(l => l.GetLastSequenceNumber(shardId, cts.Token), Times.Once);
        var kinesisInvocation = Assert.Single(kinesis.Invocations);
        var kinesisRequest = kinesisInvocation.Arguments[0] as GetShardIteratorRequest;
        Assert.NotNull(kinesisRequest);
        Assert.Equal(options.StreamArn, kinesisRequest.StreamARN);
        Assert.Equal(shardId, kinesisRequest.ShardId);
        Assert.Null(kinesisRequest.StartingSequenceNumber);
        Assert.Equal(ShardIteratorType.TRIM_HORIZON, kinesisRequest.ShardIteratorType);
        kinesis.Verify(a => a.GetShardIteratorAsync(It.IsAny<GetShardIteratorRequest>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Test_GetCheckpoint_LongTerm()
    {
        var shortTerm = new Mock<IShortTermIteratorStorage>();
        var longTerm = new Mock<ISequenceNumberStorage>();
        var kinesis = new Mock<IAmazonKinesis>();
        var options = new KinesisConfiguration
        {
            BatchSize = 0,
            StreamArn = Guid.NewGuid().ToString()
        };

        var checkpointStorage =
            new CheckpointStorage(shortTerm.Object, longTerm.Object, kinesis.Object, Options.Create(options));

        var iteratorString = Guid.NewGuid().ToString();
        var sequenceNumber = Guid.NewGuid().ToString();
        shortTerm.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?) null);
        longTerm.Setup(l => l.GetLastSequenceNumber(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sequenceNumber);
        kinesis.Setup(a => a.GetShardIteratorAsync(It.IsAny<GetShardIteratorRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetShardIteratorResponse
            {
                ShardIterator = iteratorString
            });

        var shardId = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();
        var value = await checkpointStorage.GetCheckpointAsync(shardId, cts.Token);

        Assert.Single(shortTerm.Invocations);
        shortTerm.Verify(s => s.GetAsync(shardId, cts.Token), Times.Once);

        Assert.Equal(iteratorString, value);

        Assert.Single(longTerm.Invocations);
        longTerm.Verify(l => l.GetLastSequenceNumber(shardId, cts.Token), Times.Once);
        var kinesisInvocation = Assert.Single(kinesis.Invocations);
        var kinesisRequest = kinesisInvocation.Arguments[0] as GetShardIteratorRequest;
        Assert.NotNull(kinesisRequest);
        Assert.Equal(options.StreamArn, kinesisRequest.StreamARN);
        Assert.Equal(shardId, kinesisRequest.ShardId);
        Assert.Equal(sequenceNumber, kinesisRequest.StartingSequenceNumber);
        Assert.Equal(ShardIteratorType.AFTER_SEQUENCE_NUMBER, kinesisRequest.ShardIteratorType);
        kinesis.Verify(a => a.GetShardIteratorAsync(It.IsAny<GetShardIteratorRequest>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Test_GetCheckpoint_ShortTerm()
    {
        var shortTerm = new Mock<IShortTermIteratorStorage>();
        var longTerm = new Mock<ISequenceNumberStorage>();
        var kinesis = new Mock<IAmazonKinesis>();
        var options = new KinesisConfiguration
        {
            BatchSize = 0,
            StreamArn = Guid.NewGuid().ToString()
        };

        var checkpointStorage =
            new CheckpointStorage(shortTerm.Object, longTerm.Object, kinesis.Object, Options.Create(options));

        var iteratorString = Guid.NewGuid().ToString();
        shortTerm.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iteratorString);

        var shardId = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();
        var value = await checkpointStorage.GetCheckpointAsync(shardId, cts.Token);

        Assert.Single(shortTerm.Invocations);
        shortTerm.Verify(s => s.GetAsync(shardId, cts.Token), Times.Once);

        Assert.Equal(iteratorString, value);

        Assert.Empty(longTerm.Invocations);
        Assert.Empty(kinesis.Invocations);
    }

    [Fact]
    public async Task Test_UpdateLongTerm()
    {
        var shortTerm = new Mock<IShortTermIteratorStorage>();
        var longTerm = new Mock<ISequenceNumberStorage>();
        var kinesis = new Mock<IAmazonKinesis>();
        var options = new KinesisConfiguration
        {
            BatchSize = 0,
            StreamArn = Guid.NewGuid().ToString()
        };

        var checkpointStorage =
            new CheckpointStorage(shortTerm.Object, longTerm.Object, kinesis.Object, Options.Create(options));

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();

        await checkpointStorage.UpdateLongTermAsync(key, value, cts.Token);
        Assert.Empty(shortTerm.Invocations);
        Assert.Single(longTerm.Invocations);
        longTerm.Verify(l => l.SetLastSequenceNumber(key, value, cts.Token), Times.Once);
        Assert.Empty(kinesis.Invocations);
    }

    [Fact]
    public async Task Test_UpdateShortTerm()
    {
        var shortTerm = new Mock<IShortTermIteratorStorage>();
        var longTerm = new Mock<ISequenceNumberStorage>();
        var kinesis = new Mock<IAmazonKinesis>();
        var options = new KinesisConfiguration
        {
            BatchSize = 0,
            StreamArn = Guid.NewGuid().ToString()
        };

        var checkpointStorage =
            new CheckpointStorage(shortTerm.Object, longTerm.Object, kinesis.Object, Options.Create(options));

        var key = Guid.NewGuid().ToString();
        var value = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();

        await checkpointStorage.UpdateShortTermAsync(key, value, cts.Token);
        Assert.Single(shortTerm.Invocations);
        shortTerm.Verify(s => s.SetAsync(key, value, cts.Token), Times.Once);
        Assert.Empty(longTerm.Invocations);
        Assert.Empty(kinesis.Invocations);
    }
}