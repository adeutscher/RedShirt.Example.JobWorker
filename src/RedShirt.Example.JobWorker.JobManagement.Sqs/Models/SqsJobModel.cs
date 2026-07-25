using Amazon.SQS.Model;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Models;

internal class SqsJobModel : IJobModel
{
    public required Message RawMessage { get; set; }
    public required string MessageId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}