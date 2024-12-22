using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;

public class JobModel : IJobModel
{
    public required string MessageId { get; init; }
    public required IJobDataModel Data { get; init; }
}