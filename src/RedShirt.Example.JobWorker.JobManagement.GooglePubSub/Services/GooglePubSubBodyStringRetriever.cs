using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal interface IGooglePubSubBodyStringRetriever
{
    string GetBody(IPubSubMessageContainer input);
}

internal class GooglePubSubBodyStringRetriever : IGooglePubSubBodyStringRetriever
{
    public string GetBody(IPubSubMessageContainer input)
    {
        return input.Message!.Message.Data.ToStringUtf8();
    }
}
