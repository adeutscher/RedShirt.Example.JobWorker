using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;
using System.Text;
using Record = Amazon.Kinesis.Model.Record;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class LowLevelStreamSourceTests
{
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
        var sorter = new Mock<ISourceMessageSorter>();
        sorter.Setup(s => s.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()))
            .Returns((List<IJobModel> input) => input);

        var streamArn = Guid.NewGuid().ToString();
        const int batchSize = 10;

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object, sorter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
            {
                BatchSize = batchSize,
                StreamArn = streamArn
            }));

        var cts = new CancellationTokenSource();
        var response = await source.GetJobsAsync("foo", cts.Token);
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Equal(2, response.Items.Count);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo"),
                cts.Token), Times.Once);

        converter.Verify(c => c.Convert(data1), Times.Once);
        converter.Verify(c => c.Convert(data2), Times.Once);
        converter.Verify(c => c.Convert(data3), Times.Once);
        converter.Verify(c => c.Convert(data4), Times.Once);
        sorter.Verify(a => a.GetSortedListOfJobs(It.IsAny<List<IJobModel>>()), Times.Once);

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
        var sorter = new Mock<ISourceMessageSorter>(MockBehavior.Strict);

        var streamArn = Guid.NewGuid().ToString();
        var batchSize = 10;

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object, sorter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
            {
                BatchSize = batchSize,
                StreamArn = streamArn
            }));

        var cts = new CancellationTokenSource();
        var response = await source.GetJobsAsync("foo", cts.Token);
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Empty(response.Items);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo"),
                cts.Token), Times.Once);
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
        var sorter = new Mock<ISourceMessageSorter>(MockBehavior.Strict);

        var streamArn = Guid.NewGuid().ToString();

        var source = new LowLevelStreamSource(kinesis.Object, converter.Object, sorter.Object,
            new NullLogger<LowLevelStreamSource>(), Options.Create(new KinesisConfiguration
            {
                BatchSize = batchSize,
                StreamArn = streamArn
            }));

        var cts = new CancellationTokenSource();
        var response = await source.GetJobsAsync("foo", cts.Token);
        Assert.True(string.IsNullOrWhiteSpace(response.IteratorString));
        Assert.Empty(response.Items);

        kinesis.Verify(a => a.GetRecordsAsync(It.IsAny<GetRecordsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        kinesis.Verify(
            a => a.GetRecordsAsync(
                It.Is<GetRecordsRequest>(r =>
                    r.StreamARN == streamArn
                    && r.Limit == batchSize
                    && r.ShardIterator == "foo"),
                cts.Token), Times.Once);
    }
}