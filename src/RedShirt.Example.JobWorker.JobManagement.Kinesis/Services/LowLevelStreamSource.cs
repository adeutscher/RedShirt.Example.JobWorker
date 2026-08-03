using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface ILowLevelStreamSource
{
    /// <summary>
    ///     Get Kinesis records from a specific shard.
    /// </summary>
    Task<StreamSourceResponse> GetJobsAsync(int batchSize, string shardName, string iteratorString,
        CancellationToken cancellationToken = default);
}

internal class LowLevelStreamSource(
    IAmazonKinesis kinesisClient,
    IKinesisRetryWrapperService retryWrapperService,
    IOptions<KinesisConfiguration> options) : ILowLevelStreamSource
{
    public async Task<StreamSourceResponse> GetJobsAsync(int batchSize, string shardName, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        GetRecordsResponse kinesisResponse;

        try
        {
            kinesisResponse = await retryWrapperService.RunAsync(ct =>
                kinesisClient.GetRecordsAsync(new GetRecordsRequest
                {
                    Limit = batchSize,
                    StreamARN = options.Value.StreamArn,
                    ShardIterator = iteratorString
                }, ct), cancellationToken);
        }
        catch (WorkerJobSourceException exception) when (exception.InnerException is ExpiredIteratorException)
        {
            return new StreamSourceResponse
            {
                IteratorString = string.Empty,
                Items = [],
                LastSequenceNumber = null
            };
        }

        var items = new List<IRawJobModel>();

        string? lastSequenceNumber = null;

        foreach (var item in kinesisResponse.Records)
        {
            var body = Encoding.UTF8.GetString(item.Data.ToArray());
            // Update last sequence number.
            // Can only do this because the result of GetRecordsAsync is in the correct order raw from the shard. 
            lastSequenceNumber = item.SequenceNumber;

            var data = new KinesisJobModel
            {
                ShardId = shardName,
                MessageId = item.SequenceNumber,
                CreatedAtUtc = DateTime.UtcNow,
                Body = body
            };

            items.Add(data);
        }

        return new StreamSourceResponse
        {
            IteratorString = kinesisResponse.NextShardIterator,
            Items = items,
            LastSequenceNumber = lastSequenceNumber
        };
    }
}