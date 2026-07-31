using Google.Cloud.PubSub.V1;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

internal interface IPubSubMessageContainer
{
    ReceivedMessage? Message { get; }
}
