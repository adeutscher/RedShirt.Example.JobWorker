using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

internal interface ILowLevelStreamSource
{
    Task<StreamSourceResponse> GetJobsAsync(int batchSize, string iteratorString,
        CancellationToken cancellationToken = default);
}

internal class LowLevelStreamSource(
    IAmazonKinesis kinesisClient,
    ISourceMessageConverter converter,
    ILogger<LowLevelStreamSource> logger,
    IOptions<KinesisConfiguration> options) : ILowLevelStreamSource
{
    public async Task<StreamSourceResponse> GetJobsAsync(int batchSize, string iteratorString,
        CancellationToken cancellationToken = default)
    {
        GetRecordsResponse kinesisResponse;

        try
        {
            kinesisResponse = await kinesisClient.GetRecordsAsync(new GetRecordsRequest
            {
                Limit = batchSize,
                StreamARN = options.Value.StreamArn,
                ShardIterator = iteratorString
            }, cancellationToken);
        }
        catch (ExpiredIteratorException)
        {
            return new StreamSourceResponse
            {
                IteratorString = string.Empty,
                Items = [],
                LastSequenceNumber = null
            };
        }

        var items = new List<IJobModel>();

        foreach (var item in kinesisResponse.Records)
        {
            var body = Encoding.UTF8.GetString(item.Data.ToArray());

            try
            {
                logger.LogTrace("Raw Kinesis message: {MessageBody}", body);

                var @object = converter.Convert(body);
                if (@object is null)
                {
                    continue;
                }

                var data = new JobModel
                {
                    MessageId = item.SequenceNumber,
                    CreatedAtUtc = DateTime.UtcNow,
                    Data = @object
                };

                items.Add(data);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error parsing Kinesis message: {MessageBody}", body);
            }
        }

        return new StreamSourceResponse
        {
            IteratorString = kinesisResponse.NextShardIterator,
            Items = items,
            LastSequenceNumber = items.LastOrDefault()?.MessageId
        };
    }
}