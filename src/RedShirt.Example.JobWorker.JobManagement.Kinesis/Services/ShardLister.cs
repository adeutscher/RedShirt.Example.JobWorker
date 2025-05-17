using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface IKinesisShardLister
{
    Task<List<string>> GetListOfShardsAsync(CancellationToken cancellationToken = default);
}

internal class KinesisShardLister(IAmazonKinesis kinesis, IOptions<KinesisConfiguration> options) : IKinesisShardLister
{
    public async Task<List<string>> GetListOfShardsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<string>();

        var continuationToken = default(string);
        do
        {
            var response = await kinesis.ListShardsAsync(new ListShardsRequest
            {
                StreamARN = options.Value.StreamArn,
                NextToken = continuationToken
            }, cancellationToken);
            list.AddRange(response.Shards.Select(s => s.ShardId));
            continuationToken = response.NextToken;
        } while (!string.IsNullOrEmpty(continuationToken));

        return list;
    }
}