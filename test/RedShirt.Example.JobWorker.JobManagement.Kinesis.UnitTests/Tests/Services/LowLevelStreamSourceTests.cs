using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using System.Text;
using Record = Amazon.Kinesis.Model.Record;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class LowLevelStreamSourceTests
{
    [Fact]
    public async Task GetJobsAsync_AllRecordsUnusable_StillTracksLastSequenceAndIterator()
    {
        const string nextIterator = "still-progress";
        var lastSequence = Guid.NewGuid().ToString();
        var nullBody = Guid.NewGuid().ToString();
        var throwBody = Guid.NewGuid().ToString();
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
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(nullBody))
                    },
                    new Record
                    {
                        SequenceNumber = lastSequence,
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(throwBody))
                    }
                ]
            });

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(nullBody)).Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(throwBody)).Throws(new InvalidOperationException("bad payload"));

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(10, "shard-c", "iterator-c",
            TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Equal(nextIterator, response.IteratorString);
        Assert.Equal(lastSequence, response.LastSequenceNumber);
        converter.Verify(c => c.Convert(nullBody), Times.Once);
        converter.Verify(c => c.Convert(throwBody), Times.Once);
    }

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
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
        Assert.Empty(converter.Invocations);

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
            {
                StreamArn = Guid.NewGuid().ToString(),
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            source.GetJobsAsync(10, "shard-d", "iterator-d", TestContext.Current.CancellationToken));

        Assert.Empty(converter.Invocations);
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

        var source = new LowLevelStreamSource(kinesis.Object, Mock.Of<ISourceMessageConverter>(MockBehavior.Strict),
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
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
        var mock1 = new Mock<IJobDataModel>().Object;
        var mock2 = new Mock<IJobDataModel>().Object;
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
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
        Assert.Same(mock1, first.Data);
        Assert.InRange(first.CreatedAtUtc, before, after);

        var second = Assert.IsType<KinesisJobModel>(response.Items[1]);
        Assert.Equal(sequenceNumber2, second.MessageId);
        Assert.Equal(sequenceNumber2, second.IdempotencyId);
        Assert.Same(mock2, second.Data);
    }

    [Fact]
    public async Task TestGetRecords()
    {
        var sequenceNumber1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var sequenceNumber2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;

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
                        SequenceNumber = Guid.NewGuid().ToString(), // moot
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data3))
                    },
                    new Record
                    {
                        SequenceNumber = Guid.NewGuid().ToString(), // moot
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(data4))
                    }
                ]
            });

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1))
            .Returns(mock1);
        converter.Setup(c => c.Convert(data2))
            .Returns(mock2);
        converter.Setup(c => c.Convert(data3))
            .Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data4))
            .Returns((string _) => throw new Exception());

        var streamArn = Guid.NewGuid().ToString();
        const int batchSize = 10;

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var response = await source.GetJobsAsync(batchSize, "foo-shard", "foo-iterator",
            TestContext.Current.CancellationToken);
        Assert.All(response.Items, r => Assert.Equal("foo-shard", (r as KinesisJobModel)!.ShardId));
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Equal(2, response.Items.Count);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo-iterator"),
                TestContext.Current.CancellationToken), Times.Once);

        converter.Verify(c => c.Convert(data1), Times.Once);
        converter.Verify(c => c.Convert(data2), Times.Once);
        converter.Verify(c => c.Convert(data3), Times.Once);
        converter.Verify(c => c.Convert(data4), Times.Once);

        Assert.Equal(sequenceNumber1, response.Items[0].MessageId);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Equal(sequenceNumber2, response.Items[1].MessageId);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Fact]
    public async Task WhenExpiredIterator_ReturnEmpty()
    {
        var kinesis = new Mock<IAmazonKinesis>(MockBehavior.Strict);
        kinesis.Setup(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new ExpiredIteratorException("A"));

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var streamArn = Guid.NewGuid().ToString();
        var batchSize = 10;

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var streamArn = Guid.NewGuid().ToString();

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
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