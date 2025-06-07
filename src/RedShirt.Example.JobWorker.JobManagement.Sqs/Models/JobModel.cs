using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Models;

internal class JobModel : IJobModel
{
    public required string MessageId { get; init; }
    public required IJobDataModel Data { get; init; }
}