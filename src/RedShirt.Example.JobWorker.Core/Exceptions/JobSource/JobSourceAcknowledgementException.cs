namespace RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

public class JobSourceAcknowledgementException : JobWorkerWrapperException
{
    public JobSourceAcknowledgementException(bool isTransient, Exception innerException) : base(isTransient,
        innerException)
    {
    }

    public JobSourceAcknowledgementException(bool isTransient, string message) : base(isTransient, message)
    {
    }
}