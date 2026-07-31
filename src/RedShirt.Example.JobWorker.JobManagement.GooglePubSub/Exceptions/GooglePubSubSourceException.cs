namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Exceptions;

public class GooglePubSubSourceException : Exception
{
    public GooglePubSubSourceException(string message) : base(message)
    {
    }

    public GooglePubSubSourceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
