using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

/// <summary>
///     Abstraction for returning list of shards on an AWS Kinesis stream.
///     Ordering of shards is subject to configured settings.
/// </summary>
internal interface IKinesisShardLister
{
    Task<List<string>> GetListOfShardsAsync(CancellationToken cancellationToken = default);
}

internal class KinesisShardLister(
    IAmazonKinesis kinesis,
    IKinesisRetryWrapperService retryWrapperService,
    IOptions<KinesisConfiguration> options) : IKinesisShardLister
{
    private readonly HashSet<string> _roundRobinSet = [];

    public async Task<List<string>> GetListOfShardsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<string>();

        var continuationToken = default(string);
        do
        {
            var token = continuationToken;
            var response = await retryWrapperService.RunAsync(ct =>
                kinesis.ListShardsAsync(new ListShardsRequest
                {
                    StreamARN = options.Value.StreamArn,
                    NextToken = token
                }, ct), cancellationToken);
            list.AddRange(response.Shards.Select(s => s.ShardId));
            continuationToken = response.NextToken;
        } while (!string.IsNullOrEmpty(continuationToken));

        if (list.Count == 0)
        {
            // Shouldn't ever happen, but just in case...
            // This is mostly here so that the round-robin logic can't possibly fail
            return list;
        }

        if (options.Value.RoundRobinShards)
        {
            // Round-robin has been requested 

            // Standardize order
            list = list.OrderBy(s => s).ToList();

            int i;
            for (i = 0; i < list.Count; i++)
            {
                // Search for the first item in the list that hasn't been added to the persistent round-robin set.
                if (_roundRobinSet.Add(list[i]))
                {
                    // Found it
                    break;
                }
            }

            if (i == list.Count)
            {
                // Went through the whole list and found nothing

                // Select the first in the list instead
                i = 0;
                _roundRobinSet.Clear();
                _roundRobinSet.Add(list[i]);
            }

            var newList = new List<string>
            {
                list[i]
            };

            // Add the rest
            newList.AddRange(list.Where((t, ii) => ii != i));

            list = newList;
        }
        else if (options.Value.ShuffleShards)
        {
            /*
             * Shuffle the shards before returning them upwards.
             * This will allow a single worker to be more balanced in
             *  which shards it accesses.
             */
            list = list.OrderBy(_ => Guid.NewGuid()).ToList();
        }

        return list;
    }
}