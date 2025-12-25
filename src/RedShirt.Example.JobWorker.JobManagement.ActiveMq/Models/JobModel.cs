using Apache.NMS;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Models;

internal class JobModel : IJobModel
{
    internal required IMessage Message { get; init; }
    public required string MessageId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}