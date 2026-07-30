namespace RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

public class JobSourceAcknowledgementException : Exception
{
    public bool IsTransient { get; private set; }

    public JobSourceAcknowledgementException(Exception innerException) : base(innerException.Message, innerException)
    {
        IsTransient = true;
    }

    public JobSourceAcknowledgementException(bool isTransient, Exception innerException) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }
}