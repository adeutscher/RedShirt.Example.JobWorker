using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;
using System.Text;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

internal interface ILowLevelStreamSource
{
    Task<StreamSourceResponse> GetJobsAsync(string iteratorString, CancellationToken cancellationToken = default);
}

internal class LowLevelStreamSource(
    IAmazonKinesis kinesisClient,
    ISourceMessageConverter converter,
    ISourceMessageSorter sorter,
    ILogger<LowLevelStreamSource> logger,
    IOptions<KinesisConfiguration> options) : ILowLevelStreamSource
{
    public async Task<StreamSourceResponse> GetJobsAsync(string iteratorString,
        CancellationToken cancellationToken = default)
    {
        var kinesisResponse = await kinesisClient.GetRecordsAsync(new GetRecordsRequest
        {
            Limit = 100,
            StreamARN = options.Value.StreamArn,
            ShardIterator = iteratorString
        }, cancellationToken);

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
                    MessageId = string.Empty,
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
            Items = sorter.GetSortedListOfJobs(items)
        };
    }
}