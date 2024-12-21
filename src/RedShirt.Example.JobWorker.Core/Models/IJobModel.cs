namespace RedShirt.Example.JobWorker.Core.Models;

public interface IJobModel
{
    string MessageId { get; }
    IJobDataModel Data { get; }
}