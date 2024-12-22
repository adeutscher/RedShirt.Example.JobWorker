using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class KinesisShardListerTests
{
    [Fact]
    public async Task Test_ListShards()
    {
        var kinesis = new Mock<IAmazonKinesis>();
        var queue = new Queue<string>();
        queue.Enqueue("foo");
        queue.Enqueue("bar");

        kinesis.Setup(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListShardsRequest _, CancellationToken _) =>
            {
                var haveNextToken = queue.TryDequeue(out var shardId);
                var response = new ListShardsResponse
                {
                    NextToken = haveNextToken ? "NEXT" : null,
                    Shards = []
                };

                if (haveNextToken)
                {
                    response.Shards.Add(new Shard
                    {
                        ShardId = shardId
                    });
                }

                return response;
            });

        var streamArn = Guid.NewGuid().ToString();
        var lister = new KinesisShardLister(kinesis.Object, Options.Create(new KinesisConfiguration
        {
            StreamArn = streamArn,
            BatchSize = 0
        }));

        var cts = new CancellationTokenSource();
        var output = await lister.GetListOfShardsAsync(cts.Token);
        Assert.Equal(2, output.Count);
        Assert.Contains("foo", output);
        Assert.Contains("bar", output);

        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), cts.Token), Times.Exactly(3));
        kinesis.Verify(a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.StreamARN == streamArn), cts.Token),
            Times.Exactly(3));
        kinesis.Verify(a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.NextToken == "NEXT"), cts.Token),
            Times.Exactly(2));
    }
}