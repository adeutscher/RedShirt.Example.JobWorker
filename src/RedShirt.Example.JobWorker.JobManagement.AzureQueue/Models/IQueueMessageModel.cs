namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

internal interface IQueueMessageModel
{
    string Body { get; }
    string MessageId { get; }
    string PopReceipt { get; }

    /// <summary>
    ///     Number of times the message has been received, including this receive.
    /// </summary>
    long DequeueCount { get; }
}