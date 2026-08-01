using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using System.Text;
using Record = Amazon.Kinesis.Model.Record;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class LowLevelStreamSourceTests
{
    [Fact]
    public async Task GetJobsAsync_EmptyRecords_ReturnsNextIteratorAndNullLastSequence()
    {
        const string nextIterator = "next-iterator";
        var streamArn = Guid.NewGuid().ToString();
        const int batchSize = 5;

        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRecordsResponse
            {
                Records = [],
                NextShardIterator = nextIterator
            });

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(batchSize, "shard-a", "iterator-a",
            TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Equal(nextIterator, response.IteratorString);
        Assert.Null(response.LastSequenceNumber);

        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "iterator-a"),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_NonExpiredIteratorException_Propagates()
    {
        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidArgumentException("bad request"));

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = Guid.NewGuid().ToString(),
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            source.GetJobsAsync(10, "shard-d", "iterator-d", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public async Task GetJobsAsync_PassesBatchSizeAsRecordLimit(int batchSize)
    {
        var streamArn = Guid.NewGuid().ToString();
        var iterator = Guid.NewGuid().ToString();

        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRecordsResponse
            {
                Records = [],
                NextShardIterator = "x"
            });

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        await source.GetJobsAsync(batchSize, "shard-e", iterator, TestContext.Current.CancellationToken);

        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == iterator),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_PropagatesNextIteratorAndLastSequenceNumber()
    {
        const string nextIterator = "continued-iterator";
        var sequenceNumber1 = Guid.NewGuid().ToString();
        var sequenceNumber2 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var streamArn = Guid.NewGuid().ToString();

        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRecordsResponse
            {
                NextShardIterator = nextIterator,
                Records =
                [
                    new Record
                    {
                        SequenceNumber = sequenceNumber1,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data1))
                    },
                    new Record
                    {
                        SequenceNumber = sequenceNumber2,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data2))
                    }
                ]
            });

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var before = DateTime.UtcNow;
        var response = await source.GetJobsAsync(10, "shard-b", "iterator-b",
            TestContext.Current.CancellationToken);
        var after = DateTime.UtcNow;

        Assert.Equal(nextIterator, response.IteratorString);
        Assert.Equal(sequenceNumber2, response.LastSequenceNumber);
        Assert.Equal(2, response.Items.Count);

        var first = Assert.IsType<KinesisJobModel>(response.Items[0]);
        Assert.Equal("shard-b", first.ShardId);
        Assert.Equal(sequenceNumber1, first.MessageId);
        Assert.Equal(sequenceNumber1, first.IdempotencyId);
        Assert.Equal(data1, first.Body);
        Assert.InRange(first.CreatedAtUtc, before, after);

        var second = Assert.IsType<KinesisJobModel>(response.Items[1]);
        Assert.Equal(sequenceNumber2, second.MessageId);
        Assert.Equal(sequenceNumber2, second.IdempotencyId);
        Assert.Equal(data2, second.Body);
    }

    [Fact]
    public async Task GetJobsAsync_ReturnsAllRecordsWithBodies_AndTracksLastSequenceAndIterator()
    {
        const string nextIterator = "still-progress";
        var lastSequence = Guid.NewGuid().ToString();
        var body1 = Guid.NewGuid().ToString();
        var body2 = Guid.NewGuid().ToString();
        var streamArn = Guid.NewGuid().ToString();

        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRecordsResponse
            {
                NextShardIterator = nextIterator,
                Records =
                [
                    new Record
                    {
                        SequenceNumber = Guid.NewGuid().ToString(),
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(body1))
                    },
                    new Record
                    {
                        SequenceNumber = lastSequence,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(body2))
                    }
                ]
            });

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(10, "shard-c", "iterator-c",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(body1, response.Items[0].Body);
        Assert.Equal(body2, response.Items[1].Body);
        Assert.Equal(nextIterator, response.IteratorString);
        Assert.Equal(lastSequence, response.LastSequenceNumber);
    }

    [Fact]
    public async Task TestGetRecords()
    {
        var sequenceNumber1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var sequenceNumber2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new GetRecordsResponse
            {
                Records =
                [
                    new Record
                    {
                        SequenceNumber = sequenceNumber1,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data1))
                    },
                    new Record
                    {
                        SequenceNumber = sequenceNumber2,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data2))
                    },
                    new Record
                    {
                        SequenceNumber = Guid.NewGuid().ToString(),
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data3))
                    },
                    new Record
                    {
                        SequenceNumber = Guid.NewGuid().ToString(),
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data4))
                    }
                ]
            });

        var streamArn = Guid.NewGuid().ToString();
        const int batchSize = 10;

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(batchSize, "foo-shard", "foo-iterator",
            TestContext.Current.CancellationToken);
        Assert.All(response.Items, r => Assert.Equal("foo-shard", (r as KinesisJobModel)!.ShardId));
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Equal(4, response.Items.Count);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo-iterator"),
                TestContext.Current.CancellationToken), Times.Once);

        Assert.Equal(sequenceNumber1, response.Items[0].MessageId);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(sequenceNumber2, response.Items[1].MessageId);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal(data3, response.Items[2].Body);
        Assert.Equal(data4, response.Items[3].Body);
    }

    [Fact]
    public async Task WhenExpiredIterator_ReturnEmpty()
    {
        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new ExpiredIteratorException("A"));

        var streamArn = Guid.NewGuid().ToString();
        var batchSize = 10;

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(batchSize, "foo-shard", "foo-iterator",
            TestContext.Current.CancellationToken);
        Assert.All(response.Items, r => Assert.Equal("foo-shard", (r as KinesisJobModel)!.ShardId));
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Empty(response.Items);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo-iterator"),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public async Task WhenExpiredIterator_ReturnEmpty_VariableBatchSize(int batchSize)
    {
        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new ExpiredIteratorException("A"));

        var streamArn = Guid.NewGuid().ToString();

        var source = new LowLevelStreamSource(kinesis.Object, Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(batchSize, "foo-shard", "foo-iterator",
            TestContext.Current.CancellationToken);
        Assert.All(response.Items, r => Assert.Equal("foo-shard", (r as KinesisJobModel)!.ShardId));
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Empty(response.Items);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo-iterator"),
                TestContext.Current.CancellationToken), Times.Once);
    }
}