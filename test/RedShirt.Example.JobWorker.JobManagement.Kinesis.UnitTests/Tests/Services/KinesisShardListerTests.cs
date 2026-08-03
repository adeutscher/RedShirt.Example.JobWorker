using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

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
        var lister = new KinesisShardLister(kinesis.Object, new PassthroughRetryWrapper(), Options.Create(
            new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, output.Count);
        Assert.Contains("foo", output);
        Assert.Contains("bar", output);

        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), TestContext.Current.CancellationToken),
            Times.Exactly(3));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.StreamARN == streamArn),
                TestContext.Current.CancellationToken),
            Times.Exactly(3));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.NextToken == "NEXT"),
                TestContext.Current.CancellationToken),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Test_ListShards_Empty()
    {
        var kinesis = new Mock<IAmazonKinesis>();

        kinesis.Setup(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListShardsRequest _, CancellationToken _) => new ListShardsResponse {Shards = []});

        var streamArn = Guid.NewGuid().ToString();
        var lister = new KinesisShardLister(kinesis.Object, new PassthroughRetryWrapper(), Options.Create(
            new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = false
            }));

        var output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(output);

        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(1));
        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), TestContext.Current.CancellationToken),
            Times.Exactly(1));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.StreamARN == streamArn),
                TestContext.Current.CancellationToken),
            Times.Exactly(1));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.NextToken == "NEXT"),
                TestContext.Current.CancellationToken),
            Times.Exactly(0));
    }

    [Fact]
    public async Task Test_ListShards_RoundRobin()
    {
        var kinesis = new Mock<IAmazonKinesis>();
        var queue = new Queue<string>();

        void ResetQueue()
        {
            queue.Enqueue("foo");
            queue.Enqueue("bar");
            queue.Enqueue("baz");
        }

        ResetQueue();

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
        var lister = new KinesisShardLister(kinesis.Object, new PassthroughRetryWrapper(), Options.Create(
            new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = true,
                ShuffleShards = false
            }));

        var roundRobinCheckSet = new HashSet<string>();
        var output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, output.Count);
        Assert.Contains("foo", output);
        Assert.Contains("bar", output);
        Assert.Contains("baz", output);
        Assert.True(roundRobinCheckSet.Add(output[0]));
        var firstFirstItem = output[0];

        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), TestContext.Current.CancellationToken),
            Times.Exactly(4));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.StreamARN == streamArn),
                TestContext.Current.CancellationToken),
            Times.Exactly(4));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.NextToken == "NEXT"),
                TestContext.Current.CancellationToken),
            Times.Exactly(3));

        /* Test round-robin-ness with follow-up invocations */
        ResetQueue();
        output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.True(roundRobinCheckSet.Add(output[0]));
        ResetQueue();
        output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.True(roundRobinCheckSet.Add(output[0]));
        // This fourth one is special because we only loaded 3 shards in. Should be a repeat of the first time
        ResetQueue();
        output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.False(roundRobinCheckSet.Add(output[0]));
        Assert.Equal(firstFirstItem, output[0]);
    }

    /// <summary>
    ///     Test with a shuffled shard list.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Test_ListShards_ShuffleShards(bool doShuffle)
    {
        var kinesis = new Mock<IAmazonKinesis>();

        var queue = new Queue<string>();
        const int bufferItemsCount = 2000;

        void PopulateQueueAction()
        {
            queue.Enqueue("foo");
            queue.Enqueue("bar");
            for (var i = 0; i < bufferItemsCount; i++)
            {
                queue.Enqueue($"{i}");
            }
        }

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
        var lister = new KinesisShardLister(kinesis.Object, new PassthroughRetryWrapper(), Options.Create(
            new KinesisConfiguration
            {
                StreamArn = streamArn,
                RoundRobinShards = false,
                ShuffleShards = doShuffle
            }));

        using var cts = new CancellationTokenSource();
        PopulateQueueAction();
        var output = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bufferItemsCount + 2, output.Count);
        Assert.Contains("foo", output);
        Assert.Contains("bar", output);
        for (var i = 0; i < bufferItemsCount; i++)
        {
            Assert.Contains($"{i}", output);
        }

        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(bufferItemsCount + 3));
        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), TestContext.Current.CancellationToken),
            Times.Exactly(bufferItemsCount + 3));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.StreamARN == streamArn),
                TestContext.Current.CancellationToken),
            Times.Exactly(bufferItemsCount + 3));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.NextToken == "NEXT"),
                TestContext.Current.CancellationToken),
            Times.Exactly(bufferItemsCount + 2));

        // Test randomness by running list again.

        Assert.Empty(queue);
        PopulateQueueAction();
        var output2 = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bufferItemsCount + 2, output2.Count);
        Assert.Contains("foo", output2);
        Assert.Contains("bar", output2);
        for (var i = 0; i < bufferItemsCount; i++)
        {
            Assert.Contains($"{i}", output2);
        }

        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2 * (bufferItemsCount + 3)));
        kinesis.Verify(a => a.ListShardsAsync(It.IsAny<ListShardsRequest>(), TestContext.Current.CancellationToken),
            Times.Exactly(2 * (bufferItemsCount + 3)));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.StreamARN == streamArn),
                TestContext.Current.CancellationToken),
            Times.Exactly(2 * (bufferItemsCount + 3)));
        kinesis.Verify(
            a => a.ListShardsAsync(It.Is<ListShardsRequest>(r => r.NextToken == "NEXT"),
                TestContext.Current.CancellationToken),
            Times.Exactly(2 * (bufferItemsCount + 2)));

        Assert.NotSame(output, output2);
        /*
         * Technically, this test has a very slim chance to randomly shuffle
         *  several thousand records the same way twice.
         *
         * In the unlikely event that this does happen with ShuffleShards == true,
         * then give the list an additional shuffle to be sure.
         *
         * Because I care about code coverage within my unit tests, intentionally writing
         * this in such a way that the `doShuffle == false` test case will fully run this block.
         *
         * If you somehow manage get the same random result on 4 different lists then either you
         *  are SPECTACULARLY unlucky or the implementation has been broken.
         */

        for (var i = 0; i < 10; i++)
        {
            if (!output.SequenceEqual(output2))
            {
                continue;
            }

            PopulateQueueAction();
            output2 = await lister.GetListOfShardsAsync(TestContext.Current.CancellationToken);
            /*
             * Skip the other double-checks, at this point in the test
             *  we trust the contents/calls of the lister.
             * Only the ordering is in question now.
             */
        }

        /*
         * If you somehow manage get the same random result on 12 different lists of several thousand
         * then either you are SPECTACULARLY unlucky or the implementation has actually been broken.
         *
         * While this technically has a chance to be an inconsistent test,
         * the odds are so insane that I'm willing to accept it.
         */

        Assert.Equal(!doShuffle, output.SequenceEqual(output2));
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