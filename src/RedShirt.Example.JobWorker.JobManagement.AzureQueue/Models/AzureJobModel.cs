using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

internal class AzureJobModel : IJobModel
{
    internal required IQueueMessageModel Message { get; init; }
    public string MessageId => Message.MessageId;
    public required DateTime CreatedAtUtc { get; init; }
    public required IJobDataModel Data { get; init; }
}