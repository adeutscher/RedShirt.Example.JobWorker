namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

internal interface IQueueMessageModel
{
    string Body { get; }
    string MessageId { get; }
    string PopReceipt { get; }
}